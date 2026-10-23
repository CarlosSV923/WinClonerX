using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Threading.Channels;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Logging;

namespace WinClonerX.Infrastructure.Windows.RawIo;

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
/// There is one <c>Read</c> and one <c>Write</c> <c>DiskOperation</c> per copy, never per range or chunk (log-events.md).
/// Their lengths are the bytes that actually moved, also when the copy fails.
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
    /// <param name="recorder">Records the <c>DiskOperation</c> events for each copy.</param>
    /// <param name="diskLabel">
    /// Disk identifier shown in the log when the source or the target does not name itself (<see cref="IDiskOperationLabel"/>).
    /// The real source and target always do.
    /// </param>
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
        CancellationToken ct,
        ICopyDigestSink? digests = null)
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

        // One Read and one Write for the whole copy. They start with the first chunk and end with the copy. The Read names the
        // source (its offsets are inside the source volume) and the Write names the target (its offsets are on the target disk).
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var state = new PipelineState(cts);
        using DiskOperationScope readScope = _recorder.Begin(LabelOf(source), "Read", plans[0].SourceOffset);
        using DiskOperationScope writeScope = _recorder.Begin(LabelOf(target), "Write", plans[0].TargetOffset);

        try
        {
            using AlignedBufferPool pool = _poolFactory();
            var channel = Channel.CreateBounded<ChunkWork>(new BoundedChannelOptions(ChannelCapacity)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait,
            });

            // The token goes only to the bodies, never to Task.Run. A failure in one side cancels cts; if the other side had not
            // started yet, Task.Run(…, cts.Token) would return a cancelled task, WhenAll would throw TaskCanceledException, and
            // the real error kept in state (e.g. SourceReadError) would be lost. The bodies record every exception in state, so
            // these tasks never fault and Rethrow below reports the first real error.
            Task reader = Task.Run(() => ReadChunksAsync(source, plans, pool, channel.Writer, digests, state, cts.Token), CancellationToken.None);
            Task writer = Task.Run(
                () => WriteChunksAsync(target, plans.Count, total, channel.Reader, pool, bytesWritten, state, cts.Token),
                CancellationToken.None);

            await Task.WhenAll(reader, writer).ConfigureAwait(false);

            // Both tasks have stopped. Return any chunk that was read but never written.
            while (channel.Reader.TryRead(out ChunkWork? undelivered))
            {
                pool.Return(undelivered.Buffer);
            }

            state.Rethrow(ct);
            readScope.Succeed();
            writeScope.Succeed();
        }
        catch (Exception ex)
        {
            readScope.Fail(ex);
            writeScope.Fail(ex);
            throw;
        }
        finally
        {
            readScope.SetLength(state.BytesRead);
            writeScope.SetLength(state.BytesWritten);
        }
    }

    private static async Task ReadChunksAsync(
        IBlockSource source,
        IReadOnlyList<ChunkPlan> plans,
        AlignedBufferPool pool,
        ChannelWriter<ChunkWork> output,
        ICopyDigestSink? digests,
        PipelineState state,
        CancellationToken token)
    {
        try
        {
            foreach (ChunkPlan plan in plans)
            {
                token.ThrowIfCancellationRequested();

                AlignedBuffer buffer = await pool.RentAsync(token).ConfigureAwait(false);
                bool handedOff = false;
                try
                {
                    await ReadChunkAsync(source, plan, buffer, token).ConfigureAwait(false);
                    state.AddRead(plan.Length);

                    // The digest of the exact bytes that the writer will write: the buffer is not changed after the read.
                    // It is computed here, on the reader, because the source is faster than the target and the writer is
                    // the bottleneck. SHA-256 with hardware support is far faster than either disk. Only for full verification.
                    digests?.Add(new ChunkDigest(
                        plan.TargetOffset,
                        plan.Length,
                        Sha256Digest.FromBytes(SHA256.HashData(buffer.Span[..plan.Length]))));
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
            }

            output.Complete();
        }
        catch (Exception ex)
        {
            state.Fail(ex);
        }
    }

    private static async Task WriteChunksAsync(
        ITargetDisk target,
        int expectedChunks,
        long total,
        ChannelReader<ChunkWork> input,
        AlignedBufferPool pool,
        IProgress<long> progress,
        PipelineState state,
        CancellationToken token)
    {
        int chunks = 0;
        try
        {
            await foreach (ChunkWork work in input.ReadAllAsync(token).ConfigureAwait(false))
            {
                try
                {
                    token.ThrowIfCancellationRequested();
                    await WriteChunkAsync(target, work, token).ConfigureAwait(false);
                }
                finally
                {
                    pool.Return(work.Buffer);
                }

                state.AddWritten(work.Plan.Length);
                chunks++;
                progress.Report(state.BytesWritten);
            }

            state.MarkCompleted(chunks == expectedChunks && state.BytesWritten == total);
        }
        catch (Exception ex)
        {
            state.Fail(ex);
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
                IoErrorClassifier.ForSourceRead(ex),
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
                IoErrorClassifier.ForTargetIo(ex),
                $"Write of {work.Plan.Length} bytes at {work.Plan.TargetOffset} failed.",
                ex);
        }
    }

    /// <summary>The name of a source or a target in the log: its own label when it has one, otherwise the label of the pipeline.</summary>
    private string LabelOf(object participant) =>
        participant is IDiskOperationLabel named ? named.DiskOperationLabel : _diskLabel;

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

                plans.Add(new ChunkPlan(
                    SourceOffset: range.Offset + position,
                    TargetOffset: rangeTargetOffset + position,
                    Length: length));

                position += length;
            }
        }

        return plans;
    }

    /// <summary>One chunk: where it is read, where it is written, and how long it is.</summary>
    private sealed record ChunkPlan(long SourceOffset, long TargetOffset, int Length);

    /// <summary>A chunk that has been read into a buffer, waiting to be written.</summary>
    private sealed record ChunkWork(ChunkPlan Plan, AlignedBuffer Buffer);

    /// <summary>
    /// Shared state of one copy. The first non-cancellation failure is kept, and every failure cancels the copy. Each byte
    /// counter has one writer task, and both are read after the tasks have stopped.
    /// </summary>
    private sealed class PipelineState(CancellationTokenSource cts)
    {
        private Exception? _first;
        private bool _completed;

        /// <summary>Bytes read from the source. Only the reader task adds to it.</summary>
        public long BytesRead { get; private set; }

        /// <summary>Bytes written to the target. Only the writer task adds to it.</summary>
        public long BytesWritten { get; private set; }

        public void AddRead(long bytes)
        {
            BytesRead += bytes;
        }

        public void AddWritten(long bytes)
        {
            BytesWritten += bytes;
        }

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
