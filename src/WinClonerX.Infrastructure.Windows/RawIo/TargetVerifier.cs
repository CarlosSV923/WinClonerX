using System.Security.Cryptography;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Infrastructure.Windows.Logging;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>Reads copied chunks from the target and checks their SHA-256 digests.</summary>
internal sealed class TargetVerifier : ITargetVerifier
{
    internal const int MaxRecordedOffsets = 10;
    internal const int MaxMismatchSamples = 3;
    private readonly DiskOperationRecorder _recorder;
    private readonly string _diskLabel;

    public TargetVerifier(DiskOperationRecorder recorder, string diskLabel)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentException.ThrowIfNullOrWhiteSpace(diskLabel);
        _recorder = recorder;
        _diskLabel = diskLabel;
    }

    public async Task<RangeComparison> VerifyAsync(
        ITargetDisk target,
        IReadOnlyList<ChunkDigest> digests,
        IProgress<long> bytesVerified,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(digests);
        ArgumentNullException.ThrowIfNull(bytesVerified);
        ct.ThrowIfCancellationRequested();
        if (digests.Count == 0)
        {
            return new RangeComparison(0, 0, [], 0, []);
        }

        foreach (ChunkDigest digest in digests)
        {
            if (digest.Length <= 0 || digest.TargetOffset % CopyPipeline.MinimumSectorBytes != 0
                || digest.Length % CopyPipeline.MinimumSectorBytes != 0)
            {
                throw new ArgumentException("A chunk digest has an invalid length, alignment, or SHA-256 hash.", nameof(digests));
            }
        }

        using DiskOperationScope scope = _recorder.Begin(
            target is IDiskOperationLabel named ? named.DiskOperationLabel : _diskLabel,
            "Read",
            digests[0].TargetOffset);
        long bytesRead = 0;
        try
        {
            return await Task.Run(async () =>
            {
                try
                {
                    int maxLength = digests.Max(digest => digest.Length);
                    using var buffer = new AlignedBuffer(maxLength, CopyPipeline.BufferAlignment);
                    using var secondBuffer = new AlignedBuffer(maxLength, CopyPipeline.BufferAlignment);
                    long verified = 0;
                    long differingBytes = 0;
                    long differingChunks = 0;
                    var offsets = new List<long>(MaxRecordedOffsets);
                    var samples = new List<ChunkMismatchSample>(MaxMismatchSamples);

                    foreach (ChunkDigest digest in digests)
                    {
                        ct.ThrowIfCancellationRequested();
                        Memory<byte> chunk = buffer.Memory[..digest.Length];
                        await ReadTargetAsync(target, digest.TargetOffset, chunk, ct).ConfigureAwait(false);
                        bytesRead += digest.Length;
                        if (!digest.Hash.Matches(SHA256.HashData(chunk.Span)))
                        {
                            differingChunks++;
                            differingBytes += digest.Length;
                            if (offsets.Count < MaxRecordedOffsets)
                            {
                                offsets.Add(digest.TargetOffset);
                            }

                            if (samples.Count < MaxMismatchSamples)
                            {
                                Memory<byte> second = secondBuffer.Memory[..digest.Length];
                                await ReadTargetAsync(target, digest.TargetOffset, second, ct).ConfigureAwait(false);
                                bytesRead += digest.Length;
                                samples.Add(new ChunkMismatchSample(
                                    digest.TargetOffset,
                                    digest.Length,
                                    CryptographicOperations.FixedTimeEquals(SHA256.HashData(chunk.Span), SHA256.HashData(second.Span))));
                            }
                        }

                        verified += digest.Length;
                        bytesVerified.Report(verified);
                    }

                    scope.Succeed();
                    return new RangeComparison(verified, differingBytes, offsets, differingChunks, samples);
                }
                catch (Exception ex)
                {
                    scope.Fail(ex);
                    throw;
                }
            }, CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            scope.SetLength(bytesRead);
        }
    }

    private static async Task ReadTargetAsync(ITargetDisk target, long offset, Memory<byte> buffer, CancellationToken ct)
    {
        int read;
        try
        {
            read = await target.ReadAsync(offset, buffer, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlatformException and not OperationCanceledException)
        {
            throw new PlatformException(IoErrorClassifier.ForTargetIo(ex), $"Read of {buffer.Length} bytes at {offset} failed.", ex);
        }

        if (read != buffer.Length)
        {
            throw new PlatformException(
                IoErrorClassifier.ForTargetIo(new IOException("Short target read.")),
                $"Short read at {offset}: {read} of {buffer.Length} bytes.");
        }
    }
}
