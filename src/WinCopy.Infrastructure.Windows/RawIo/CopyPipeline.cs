using System.Runtime.ExceptionServices;
using System.Threading.Channels;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Logging;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Copies the used ranges of a source to the target disk through a read and write pipeline (R3).
/// </summary>
/// <remarks>
/// <para>
/// A reader task fills rented <see cref="AlignedBuffer"/>s and posts them to a bounded channel. A writer task
/// writes them to the target and returns each buffer to the pool. With a capacity of two and a pool of four, two to four
/// buffers are in flight. Every chunk is at most 4 MiB. 4 MiB is a multiple of 512 and of 4096, so every chunk is
/// sector-aligned for either sector size.
/// </para>
/// <para>
/// The first failure wins. A source error stays a <see cref="PlatformErrorCode.SourceReadError"/>, and a target error
/// stays a <see cref="PlatformErrorCode.TargetWriteError"/>. Any failure cancels both tasks, so no write starts after it.
/// Buffers are always returned, and the pool is disposed when the copy ends.
/// </para>
/// <para>
/// There is one <c>Read</c> and one <c>Write</c> <c>DiskOperation</c> per range, never per chunk (log-events.md).
/// </para>
/// </remarks>
internal sealed class CopyPipeline : IVolumeCopier
{
    /// <summary>Maximum size of one chunk.</summary>
    internal const int ChunkBytes = 4 * 1024 * 1024;

    /// <summary>Capacity of the channel between the reader and the writer.</summary>
    internal const int ChannelCapacity = 2;

    /// <summary>Alignment of the pooled buffers.</summary>
    internal const int BufferAlignment = 4096;

    /// <summary>Smallest legal sector size. Every range must be aligned to it.</summary>
    internal const int MinimumSectorBytes = 512;

    private readonly Func<AlignedBufferPool> _poolFactory;
    private readonly DiskOperationRecorder _recorder;
    private readonly string _diskLabel;

    /// <param name="poolFactory">Creates the buffer pool for one copy. The pipeline disposes it when the copy ends.</param>
    /// <param name="recorder">Records the <c>DiskOperation</c> events for each range.</param>
    /// <param name="diskLabel">Disk identifier shown in the log.</param>
    public CopyPipeline(Func<AlignedBufferPool> poolFactory, DiskOperationRecorder recorder, string diskLabel)
    {
        ArgumentNullException.ThrowIfNull(poolFactory);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentException.ThrowIfNullOrWhiteSpace(diskLabel);

        _poolFactory = poolFactory;
        _recorder = recorder;
        _diskLabel = diskLabel;
    }

    /// <summary>Production pool: four buffers of 4 MiB, aligned to 4096 bytes.</summary>
    public static AlignedBufferPool CreateDefaultPool() =>
        new((nuint)BufferAlignment, AlignedBufferPool.DefaultBufferCount, AlignedBufferPool.DefaultBufferSize);

    /// <inheritdoc />
    public async Task CopyAsync(
        IBlockSource source,
        VolumeAllocation allocation,
        ITargetDisk target,
        long targetOffset,
        IProgress<long> bytesWritten,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(allocation);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(bytesWritten);
        ct.ThrowIfCancellationRequested();

        IReadOnlyList<ChunkPlan> plans = PlanChunks(allocation, targetOffset);
        if (plans.Count == 0)
        {
            return;
        }

        long total = allocation.TotalUsed.Bytes;

        using AlignedBufferPool pool = _poolFactory();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var channel = Channel.CreateBounded<ChunkWork>(new BoundedChannelOptions(ChannelCapacity)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait,
        });

        var state = new PipelineState(cts);

        Task reader = Task.Run(() => ReadChunksAsync(source, plans, pool, channel.Writer, state, cts.Token), cts.Token);
        Task writer = Task.Run(
            () => WriteChunksAsync(target, plans.Count, total, channel.Reader, pool, bytesWritten, state, cts.Token),
            cts.Token);

        await Task.WhenAll(reader, writer).ConfigureAwait(false);

        // Both tasks have stopped. Return any chunk that was read but never written.
        while (channel.Reader.TryRead(out ChunkWork? undelivered))
        {
            pool.Return(undelivered.Buffer);
        }

        state.Rethrow(ct);
    }

    private async Task ReadChunksAsync(
        IBlockSource source,
        IReadOnlyList<ChunkPlan> plans,
        AlignedBufferPool pool,
        ChannelWriter<ChunkWork> output,
        PipelineState state,
        CancellationToken token)
    {
        DiskOperationScope? scope = null;
        try
        {
            foreach (ChunkPlan plan in plans)
            {
                token.ThrowIfCancellationRequested();

                if (plan.IsFirstOfRange)
                {
                    scope = _recorder.Begin(_diskLabel, "Read", plan.RangeSourceOffset, plan.RangeLength);
                }

                AlignedBuffer buffer = await pool.RentAsync(token).ConfigureAwait(false);
                bool handedOff = false;
                try
                {
                    await ReadChunkAsync(source, plan, buffer, token).ConfigureAwait(false);
                    await output.WriteAsync(new ChunkWork(plan, buffer), token).ConfigureAwait(false);
                    handedOff = true;
                }
                finally
                {
                    if (!handedOff)
                    {
                        pool.Return(buffer);
                    }
                }

                if (plan.IsLastOfRange)
                {
                    scope?.Succeed();
                    scope?.Dispose();
                    scope = null;
                }
            }

            output.Complete();
        }
        catch (Exception ex)
        {
            scope?.Fail(ex);
            state.Fail(ex);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private async Task WriteChunksAsync(
        ITargetDisk target,
        int expectedChunks,
        long total,
        ChannelReader<ChunkWork> input,
        AlignedBufferPool pool,
        IProgress<long> progress,
        PipelineState state,
        CancellationToken token)
    {
        DiskOperationScope? scope = null;
        long written = 0;
        int chunks = 0;
        try
        {
            await foreach (ChunkWork work in input.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    if (work.Plan.IsFirstOfRange)
                    {
                        scope = _recorder.Begin(_diskLabel, "Write", work.Plan.RangeTargetOffset, work.Plan.RangeLength);
                    }

                    await WriteChunkAsync(target, work, token).ConfigureAwait(false);
                }
                finally
                {
                    pool.Return(work.Buffer);
                }

                written += work.Plan.Length;
                chunks++;
                progress.Report(written);

                if (work.Plan.IsLastOfRange)
                {
                    scope?.Succeed();
                    scope?.Dispose();
                    scope = null;
                }
            }

            state.MarkCompleted(chunks == expectedChunks && written == total);
        }
        catch (Exception ex)
        {
            scope?.Fail(ex);
            state.Fail(ex);
        }
        finally
        {
            scope?.Dispose();
        }
    }

    private static async Task ReadChunkAsync(IBlockSource source, ChunkPlan plan, AlignedBuffer buffer, CancellationToken token)
    {
        int read;
        try
        {
            read = await source.ReadAsync(plan.SourceOffset, buffer.Memory[..plan.Length], token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlatformException and not OperationCanceledException)
        {
            throw new PlatformException(
                PlatformErrorCode.SourceReadError,
                $"Read of {plan.Length} bytes at {plan.SourceOffset} failed.",
                ex);
        }

        if (read != plan.Length)
        {
            throw new PlatformException(
                PlatformErrorCode.SourceReadError,
                $"Short read at {plan.SourceOffset}: {read} of {plan.Length} bytes.");
        }
    }

    private static async Task WriteChunkAsync(ITargetDisk target, ChunkWork work, CancellationToken token)
    {
        try
        {
            await target.WriteAsync(work.Plan.TargetOffset, work.Buffer.Memory[..work.Plan.Length], token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not PlatformException and not OperationCanceledException)
        {
            throw new PlatformException(
                PlatformErrorCode.TargetWriteError,
                $"Write of {work.Plan.Length} bytes at {work.Plan.TargetOffset} failed.",
                ex);
        }
    }

    private static List<ChunkPlan> PlanChunks(VolumeAllocation allocation, long targetOffset)
    {
        var plans = new List<ChunkPlan>();

        foreach (Extent range in allocation.UsedRanges)
        {
            if (range.Offset % MinimumSectorBytes != 0 || range.Length % MinimumSectorBytes != 0)
            {
                throw new ArgumentException(
                    $"Range {range.Offset}+{range.Length} is not aligned to {MinimumSectorBytes} bytes.",
                    nameof(allocation));
            }

            long rangeTargetOffset = checked(targetOffset + range.Offset);
            long position = 0;
            while (position < range.Length)
            {
                int length = (int)Math.Min(ChunkBytes, range.Length - position);
                bool isLast = position + length == range.Length;

                plans.Add(new ChunkPlan(
                    SourceOffset: range.Offset + position,
                    TargetOffset: rangeTargetOffset + position,
                    Length: length,
                    RangeSourceOffset: range.Offset,
                    RangeTargetOffset: rangeTargetOffset,
                    RangeLength: range.Length,
                    IsFirstOfRange: position == 0,
                    IsLastOfRange: isLast));

                position += length;
            }
        }

        return plans;
    }

    /// <summary>One chunk of one range: where it is read, where it is written, and the range it belongs to.</summary>
    private sealed record ChunkPlan(
        long SourceOffset,
        long TargetOffset,
        int Length,
        long RangeSourceOffset,
        long RangeTargetOffset,
        long RangeLength,
        bool IsFirstOfRange,
        bool IsLastOfRange);

    /// <summary>A chunk that has been read into a buffer, waiting to be written.</summary>
    private sealed record ChunkWork(ChunkPlan Plan, AlignedBuffer Buffer);

    /// <summary>Shared state of one copy. The first non-cancellation failure is kept, and every failure cancels the copy.</summary>
    private sealed class PipelineState(CancellationTokenSource cts)
    {
        private Exception? _first;
        private bool _completed;

        public void Fail(Exception ex)
        {
            if (ex is not OperationCanceledException)
            {
                Interlocked.CompareExchange(ref _first, ex, null);
            }

            cts.Cancel();
        }

        public void MarkCompleted(bool completed)
        {
            _completed = completed;
        }

        public void Rethrow(CancellationToken ct)
        {
            if (_first is not null)
            {
                ExceptionDispatchInfo.Throw(_first);
            }

            if (!_completed)
            {
                throw new OperationCanceledException(ct);
            }
        }
    }
}
