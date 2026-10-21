using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Logging;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Compares the used ranges of a source volume with the bytes the copy wrote to the target (verification completa, FR-022a).
/// </summary>
/// <remarks>
/// <para>
/// Each range is read from the source and from the target in chunks of at most <see cref="CopyPipeline.ChunkBytes"/>, into two
/// aligned buffers that are allocated once per comparison. Memory is therefore bounded at two chunks, whatever the size of the
/// partition. Only the ranges of the allocation are read: unused clusters were never copied, so they are not compared.
/// </para>
/// <para>
/// The source is read through <see cref="IBlockSource"/> and the target through <see cref="ITargetDisk.ReadAsync"/>, so the
/// comparison never writes. A read that fails is a <see cref="PlatformException"/>, with the same codes as the copy: a source
/// failure is <see cref="PlatformErrorCode.SourceReadError"/> (or a source device removal), and a target failure is
/// classified by <see cref="IoErrorClassifier.ForTargetIo"/>. A byte that differs is not an error: it is counted, and its
/// offset is kept for the first <see cref="MaxRecordedOffsets"/> differences.
/// </para>
/// <para>
/// One <c>DiskOperation</c> <c>Read</c> is logged per comparison, naming the target disk, with the absolute target offset of the
/// first range and the bytes read from the target (log-events.md). Nothing is logged for an allocation without ranges.
/// </para>
/// </remarks>
internal sealed class VolumeComparer : IVolumeComparer
{
    /// <summary>Número de desplazamientos distintos que se guardan por comparación.</summary>
    internal const int MaxRecordedOffsets = 10;

    private readonly DiskOperationRecorder _recorder;
    private readonly string _diskLabel;
    private readonly int _chunkBytes;

    /// <param name="recorder">Records the <c>DiskOperation</c> event of each comparison.</param>
    /// <param name="diskLabel">
    /// Disk identifier shown in the log when the target does not name itself (<see cref="IDiskOperationLabel"/>). The real target
    /// always does.
    /// </param>
    public VolumeComparer(DiskOperationRecorder recorder, string diskLabel)
        : this(recorder, diskLabel, CopyPipeline.ChunkBytes)
    {
    }

    /// <param name="chunkBytes">Size of each comparison chunk. A positive multiple of the buffer alignment; tests use small values.</param>
    internal VolumeComparer(DiskOperationRecorder recorder, string diskLabel, int chunkBytes)
    {
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentException.ThrowIfNullOrWhiteSpace(diskLabel);

        if (chunkBytes <= 0 || chunkBytes % CopyPipeline.BufferAlignment != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkBytes), chunkBytes, "The chunk must be a positive multiple of the alignment.");
        }

        _recorder = recorder;
        _diskLabel = diskLabel;
        _chunkBytes = chunkBytes;
    }

    /// <inheritdoc />
    public async Task<RangeComparison> CompareAsync(
        IBlockSource source,
        VolumeAllocation allocation,
        ITargetDisk target,
        long targetOffset,
        IProgress<long> bytesCompared,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bytesCompared);
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<Extent> ranges = allocation.UsedRanges;
        if (ranges.Count == 0)
        {
            return new RangeComparison(0, 0, []);
        }

        foreach (Extent range in ranges)
        {
            if (range.Offset % CopyPipeline.MinimumSectorBytes != 0 || range.Length % CopyPipeline.MinimumSectorBytes != 0)
            {
                throw new ArgumentException(
                    $"Range {range.Offset}+{range.Length} is not aligned to {CopyPipeline.MinimumSectorBytes} bytes.",
                    nameof(allocation));
            }
        }

        using DiskOperationScope scope = _recorder.Begin(LabelOf(target), "Read", checked(targetOffset + ranges[0].Offset));
        long targetBytesRead = 0;

        try
        {
            using var sourceBuffer = new AlignedBuffer(_chunkBytes, CopyPipeline.BufferAlignment);
            using var targetBuffer = new AlignedBuffer(_chunkBytes, CopyPipeline.BufferAlignment);

            long compared = 0;
            long differing = 0;
            var offsets = new List<long>(MaxRecordedOffsets);

            foreach (Extent range in ranges)
            {
                for (long position = 0; position < range.Length; position += _chunkBytes)
                {
                    ct.ThrowIfCancellationRequested();

                    int length = (int)Math.Min(_chunkBytes, range.Length - position);
                    long sourceOffset = range.Offset + position;

                    await ReadSourceAsync(source, sourceOffset, sourceBuffer.Memory[..length], ct).ConfigureAwait(false);
                    await ReadTargetAsync(target, checked(targetOffset + sourceOffset), targetBuffer.Memory[..length], ct)
                        .ConfigureAwait(false);
                    targetBytesRead += length;

                    differing += CountDifferences(sourceBuffer.Span[..length], targetBuffer.Span[..length], sourceOffset, offsets);

                    compared += length;
                    bytesCompared.Report(compared);
                }
            }

            scope.Succeed();
            return new RangeComparison(compared, differing, offsets);
        }
        catch (Exception ex)
        {
            scope.Fail(ex);
            throw;
        }
        finally
        {
            scope.SetLength(targetBytesRead);
        }
    }

    /// <summary>
    /// Counts the bytes that differ between two equally long chunks. The first <see cref="MaxRecordedOffsets"/> differences of the
    /// comparison are added to <paramref name="offsets"/>, as offsets in the source (<paramref name="sourceOffset"/> is the start
    /// of the chunk there). Equal chunks take the fast path, so the byte loop runs only where something differs.
    /// </summary>
    private static long CountDifferences(ReadOnlySpan<byte> source, ReadOnlySpan<byte> target, long sourceOffset, List<long> offsets)
    {
        if (source.SequenceEqual(target))
        {
            return 0;
        }

        long count = 0;
        for (int i = 0; i < source.Length; i++)
        {
            if (source[i] == target[i])
            {
                continue;
            }

            count++;
            if (offsets.Count < MaxRecordedOffsets)
            {
                offsets.Add(sourceOffset + i);
            }
        }

        return count;
    }

    private static async Task ReadSourceAsync(IBlockSource source, long offset, Memory<byte> buffer, CancellationToken ct)
    {
        int read;
        try
        {
            read = await source.ReadAsync(offset, buffer, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlatformException and not OperationCanceledException)
        {
            throw new PlatformException(
                IoErrorClassifier.ForSourceRead(ex),
                $"Read of {buffer.Length} bytes at {offset} failed.",
                ex);
        }

        if (read != buffer.Length)
        {
            throw new PlatformException(
                PlatformErrorCode.SourceReadError,
                $"Short read at {offset}: {read} of {buffer.Length} bytes.");
        }
    }

    private static async Task ReadTargetAsync(ITargetDisk target, long diskOffset, Memory<byte> buffer, CancellationToken ct)
    {
        int read;
        try
        {
            read = await target.ReadAsync(diskOffset, buffer, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlatformException and not OperationCanceledException)
        {
            throw new PlatformException(
                IoErrorClassifier.ForTargetIo(ex),
                $"Read of {buffer.Length} bytes at {diskOffset} failed.",
                ex);
        }

        // There is no read-specific code for the target. TargetDisk reports a short target read the same way (TargetWriteError).
        if (read != buffer.Length)
        {
            throw new PlatformException(
                PlatformErrorCode.TargetWriteError,
                $"Short read at {diskOffset}: {read} of {buffer.Length} bytes.");
        }
    }

    /// <summary>The name of the target in the log: its own label when it has one, otherwise the label of this comparer.</summary>
    private string LabelOf(object participant) =>
        participant is IDiskOperationLabel named ? named.DiskOperationLabel : _diskLabel;
}
