using System.Security.Cryptography;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Planning;
using WinClonerX.Infrastructure.Windows.Logging;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Tests.Fakes;
using WinClonerX.Infrastructure.Windows.Tests.Logging;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

public sealed class TargetVerifierTests
{
    private const int Chunk = 4096;
    private const long TargetOffset = 1024 * 1024;

    [Fact]
    public void ChunkDigestIsStoredAsACompact48ByteValue()
    {
        Marshal.SizeOf<ChunkDigest>().ShouldBe(48);
    }

    [Fact]
    public async Task MatchingTargetPassesAndLogsOneReadOperation()
    {
        byte[] bytes = Pattern(Chunk);
        var target = new MemoryTarget(bytes, TargetOffset + Chunk);
        var log = new RecordingExecutionLog();

        RangeComparison result = await NewVerifier(log).VerifyAsync(target, [Digest(bytes)], new Progress<long>(_ => { }), CancellationToken.None);

        result.BytesCompared.ShouldBe(Chunk);
        result.DifferingChunks.ShouldBe(0);
        result.DifferingBytes.ShouldBe(0);
        log.Events.Single().Properties["Operation"].ShouldBe("Read");
    }

    [Fact]
    public async Task FlippedByteFailsTheWholeChunkAndRecordsItsTargetOffset()
    {
        byte[] bytes = Pattern(Chunk);
        var target = new MemoryTarget(bytes, TargetOffset + Chunk);
        target.Image[(int)TargetOffset + 12] ^= 1;

        RangeComparison result = await NewVerifier().VerifyAsync(target, [Digest(bytes)], new Progress<long>(_ => { }), CancellationToken.None);

        result.DifferingChunks.ShouldBe(1);
        result.DifferingBytes.ShouldBe(Chunk);
        result.FirstDifferenceOffsets.ShouldBe([TargetOffset]);
    }

    [Fact]
    public async Task TargetReadsRunOffTheCallingThread()
    {
        byte[] bytes = Pattern(Chunk);
        int callerThread = 0;
        int readThread = 0;
        Task<RangeComparison>? verification = null;
        var caller = new Thread(() =>
        {
            callerThread = Environment.CurrentManagedThreadId;
            verification = NewVerifier().VerifyAsync(
                new MemoryTarget(bytes, TargetOffset + Chunk, () => readThread = Environment.CurrentManagedThreadId),
                [Digest(bytes)], new Progress<long>(_ => { }), CancellationToken.None);
        });
        caller.Start();
        caller.Join();

        await verification!;
        readThread.ShouldNotBe(callerThread);
    }

    [Fact]
    public async Task MismatchDiagnosticDetectsAnUnstableTargetRead()
    {
        byte[] bytes = Pattern(Chunk);
        var target = new MemoryTarget(bytes, TargetOffset + Chunk);
        target.Image[(int)TargetOffset] ^= 1;
        target.MutateAfterFirstRead = true;

        RangeComparison result = await NewVerifier().VerifyAsync(target, [Digest(bytes)], new Progress<long>(_ => { }), CancellationToken.None);

        result.MismatchSamples!.Single().TargetStable.ShouldBeFalse();
    }

    [Fact]
    public async Task ShortTargetReadIsClassifiedAsTargetIoError()
    {
        byte[] bytes = Pattern(Chunk);
        var target = new MemoryTarget(bytes, TargetOffset + Chunk) { ShortRead = true };

        PlatformException error = await Should.ThrowAsync<PlatformException>(() =>
            NewVerifier().VerifyAsync(target, [Digest(bytes)], new Progress<long>(_ => { }), CancellationToken.None));

        error.Code.ShouldBe(WinClonerX.Application.Errors.PlatformErrorCode.TargetWriteError);
    }

    [Fact]
    public async Task TargetReadFailureUsesTargetIoClassification()
    {
        byte[] bytes = Pattern(Chunk);
        var target = new MemoryTarget(bytes, TargetOffset + Chunk) { ReadFailure = new Win32Exception(1167) };

        PlatformException error = await Should.ThrowAsync<PlatformException>(() =>
            NewVerifier().VerifyAsync(target, [Digest(bytes)], new Progress<long>(_ => { }), CancellationToken.None));

        error.Code.ShouldBe(WinClonerX.Application.Errors.PlatformErrorCode.TargetDeviceRemoved);
    }

    private static TargetVerifier NewVerifier(RecordingExecutionLog? log = null) =>
        new(new DiskOperationRecorder(log ?? new RecordingExecutionLog(), new ManualClock(DateTimeOffset.UnixEpoch)), "target");

    private static ChunkDigest Digest(byte[] bytes) => new(TargetOffset, bytes.Length, Sha256Digest.FromBytes(SHA256.HashData(bytes)));

    private static byte[] Pattern(int length) => Enumerable.Range(0, length).Select(i => (byte)((i * 31 + 7) % 251 + 1)).ToArray();

    private sealed class MemoryTarget : ITargetDisk
    {
        private readonly Action? _onRead;
        private int _reads;
        public byte[] Image { get; }
        public bool MutateAfterFirstRead { get; set; }
        public bool ShortRead { get; set; }
        public Exception? ReadFailure { get; set; }

        public MemoryTarget(byte[] bytes, long imageSize, Action? onRead = null)
        {
            Image = new byte[imageSize];
            _onRead = onRead;
            bytes.CopyTo(Image, (int)TargetOffset);
        }

        public ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct)
        {
            _onRead?.Invoke();
            if (ReadFailure is not null)
            {
                return ValueTask.FromException<int>(ReadFailure);
            }

            Image.AsMemory((int)diskOffset, alignedBuffer.Length).CopyTo(alignedBuffer);
            if (MutateAfterFirstRead && Interlocked.Increment(ref _reads) == 1)
            {
                Image[(int)diskOffset] ^= 1;
            }

            return ValueTask.FromResult(ShortRead ? alignedBuffer.Length - 512 : alignedBuffer.Length);
        }

        public Task LockAndTakeOfflineAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task WipeBoundariesAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct) => throw new NotSupportedException();
        public Task CommitLayoutAsync(TargetLayout layout, CancellationToken ct) => throw new NotSupportedException();
        public Task BringOnlineAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task DeleteLayoutAsync(CancellationToken ct) => throw new NotSupportedException();
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
