using System.ComponentModel;
using System.Reflection;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Logging;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Tests.Fakes;
using WinClonerX.Infrastructure.Windows.Tests.Logging;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// Source and target are in memory. Failures are injected by index, so the error and cancellation cases are deterministic.
public sealed class CopyPipelineTests
{
    private const long Mib = Sizes.Mib;
    private const int ChunkMib4 = 4 * 1024 * 1024;
    private const string SnapshotLabel = "snapshot of {4a1f9c2e-1d7b-4e3a-9b8f-0c2d5e6f7a8b}";

    [Fact]
    public async Task CopiesEveryRangeToItsTargetOffsetByteExactAndLeavesTheRestZero()
    {
        byte[] source = Pattern(16 * Mib);
        Extent[] ranges = [new(0, 4096), new(1 * Mib, 2 * Mib), new(8 * Mib, Mib + 512)];
        const long targetBase = 1 * Mib;
        var target = new MemoryTargetDisk(12 * Mib);
        var pipeline = NewPipeline();

        await pipeline.CopyAsync(new MemoryBlockSource(source, Allocation(ranges)), Allocation(ranges), target, targetBase, new RecordingProgress(), CancellationToken.None);

        byte[] expected = new byte[target.Image.Length];
        foreach (Extent range in ranges)
        {
            source.AsSpan((int)range.Offset, (int)range.Length).CopyTo(expected.AsSpan((int)(targetBase + range.Offset)));
        }

        target.Image.AsSpan().SequenceEqual(expected).ShouldBeTrue("the target must hold each range at targetBase + range.Offset, and zeros elsewhere");
    }

    [Fact]
    public async Task ChunksAreAtMost4MiBAndCoverTheRange()
    {
        Extent[] ranges = [new(0, 10 * Mib + 4096)];
        var target = new MemoryTargetDisk(16 * Mib);

        await NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), target, 0, new RecordingProgress(), CancellationToken.None);

        target.Attempts.Select(a => a.Length).ToArray().ShouldBe([ChunkMib4, ChunkMib4, 2 * 1024 * 1024 + 4096]);
        target.Attempts.Select(a => a.Offset).ToArray().ShouldBe([0L, 4 * Mib, 8 * Mib]);
    }

    [Fact]
    public async Task ProgressIsMonotonicAndEndsAtTheTotal()
    {
        Extent[] ranges = [new(0, 6 * Mib), new(8 * Mib, Mib)];
        var progress = new RecordingProgress();

        await NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), new MemoryTargetDisk(16 * Mib), 0, progress, CancellationToken.None);

        progress.Values.ShouldBe([4 * Mib, 6 * Mib, 7 * Mib]);
        progress.Values.Zip(progress.Values.Skip(1)).All(pair => pair.Second >= pair.First).ShouldBeTrue();
    }

    [Fact]
    public async Task SourceErrorAbortsWithSourceReadErrorAndNoLaterWrite()
    {
        Extent[] ranges = [new(0, 12 * Mib)];
        var target = new MemoryTargetDisk(16 * Mib);
        var source = new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges), failOnRead: 2);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            NewPipeline().CopyAsync(source, Allocation(ranges), target, 0, new RecordingProgress(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
        target.Attempts.Select(a => a.Offset).ShouldNotContain(8 * Mib, "the chunk whose read failed must never be written");
        target.Attempts.Count.ShouldBeLessThan(3);
    }

    [Fact]
    public async Task NonPlatformSourceExceptionIsWrappedAsSourceReadError()
    {
        Extent[] ranges = [new(0, 4 * Mib)];
        var source = new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges), failOnRead: 0, failure: new IOException("disk gone"));

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            NewPipeline().CopyAsync(source, Allocation(ranges), new MemoryTargetDisk(16 * Mib), 0, new RecordingProgress(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
        ex.InnerException.ShouldBeOfType<IOException>();
    }

    [Theory]
    [InlineData(1167, PlatformErrorCode.SourceDeviceRemoved)]
    [InlineData(433, PlatformErrorCode.SourceDeviceRemoved)]
    [InlineData(1117, PlatformErrorCode.SourceReadError)]
    [InlineData(23, PlatformErrorCode.SourceReadError)]
    public async Task SourceReadFailureIsClassifiedByItsWin32Code(int nativeError, PlatformErrorCode expected)
    {
        Extent[] ranges = [new(0, 4 * Mib)];
        var source = new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges), failOnRead: 0, failure: new Win32Exception(nativeError));

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            NewPipeline().CopyAsync(source, Allocation(ranges), new MemoryTargetDisk(16 * Mib), 0, new RecordingProgress(), CancellationToken.None));

        ex.Code.ShouldBe(expected);
    }

    [Theory]
    [InlineData(1167, PlatformErrorCode.TargetDeviceRemoved)]
    [InlineData(21, PlatformErrorCode.TargetDeviceRemoved)]
    [InlineData(29, PlatformErrorCode.TargetWriteError)]
    [InlineData(121, PlatformErrorCode.TargetWriteError)]
    public async Task TargetWriteFailureIsClassifiedByItsWin32Code(int nativeError, PlatformErrorCode expected)
    {
        Extent[] ranges = [new(0, 16 * Mib)];
        var target = new MemoryTargetDisk(16 * Mib, failOnWrite: 0, writeFailure: new Win32Exception(nativeError));

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), target, 0, new RecordingProgress(), CancellationToken.None));

        ex.Code.ShouldBe(expected);
    }

    [Fact]
    public async Task TargetErrorAbortsWithTargetWriteErrorAndNoFurtherWrite()
    {
        Extent[] ranges = [new(0, 16 * Mib)];
        var target = new MemoryTargetDisk(16 * Mib, failOnWrite: 1);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), target, 0, new RecordingProgress(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.TargetWriteError);
        target.Attempts.Count.ShouldBe(2, "the first write succeeds, the second fails, and nothing is written after it");
    }

    [Fact]
    public async Task CancellationStopsTheCopyBeforeTheNextWrite()
    {
        Extent[] ranges = [new(0, 16 * Mib)];
        using var cts = new CancellationTokenSource();
        var target = new MemoryTargetDisk(16 * Mib, onWrite: index =>
        {
            if (index == 0)
            {
                cts.Cancel();
            }
        });

        await Should.ThrowAsync<OperationCanceledException>(() =>
            NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), target, 0, new RecordingProgress(), cts.Token));

        target.Attempts.Count.ShouldBe(1);
    }

    [Fact]
    public async Task AnAlreadyCancelledTokenWritesNothing()
    {
        Extent[] ranges = [new(0, 4 * Mib)];
        var target = new MemoryTargetDisk(16 * Mib);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), target, 0, new RecordingProgress(), cts.Token));

        target.Attempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task OneReadAndOneWriteOperationIsRecordedPerCopyNotPerRangeOrChunk()
    {
        Extent[] ranges = [new(0, 6 * Mib), new(8 * Mib, Mib)];
        var log = new RecordingExecutionLog();

        await NewPipeline(log: log).CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), new MemoryTargetDisk(16 * Mib), 0, new RecordingProgress(), CancellationToken.None);

        var operations = Operations(log);
        operations.Count(o => o == "Read").ShouldBe(1);
        operations.Count(o => o == "Write").ShouldBe(1);
    }

    [Fact]
    public async Task ManyRangesOfOneCopyYieldOneReadAndOneWriteWithTheSummedLengths()
    {
        const int rangeCount = 50;
        const long rangeLength = 4096;
        const long targetBase = 1 * Mib;
        Extent[] ranges = Enumerable.Range(0, rangeCount).Select(i => new Extent(i * 8192L, rangeLength)).ToArray();
        var log = new RecordingExecutionLog();

        await NewPipeline(log: log).CopyAsync(new MemoryBlockSource(Pattern(512 * 1024), Allocation(ranges)), Allocation(ranges), new MemoryTargetDisk(2 * Mib), targetBase, new RecordingProgress(), CancellationToken.None);

        var reads = DiskOperationEvents(log, "Read");
        var writes = DiskOperationEvents(log, "Write");
        reads.Count.ShouldBe(1);
        writes.Count.ShouldBe(1);
        ((long)reads[0]["Offset"]!).ShouldBe(0L);
        ((long)reads[0]["Length"]!).ShouldBe(rangeCount * rangeLength);
        ((long)writes[0]["Offset"]!).ShouldBe(targetBase);
        ((long)writes[0]["Length"]!).ShouldBe(rangeCount * rangeLength);
    }

    [Fact]
    public async Task TheReadNamesTheSourceAndTheWriteNamesTheTarget()
    {
        Extent[] ranges = [new(0, 4 * Mib)];
        var log = new RecordingExecutionLog();
        var source = new LabeledBlockSource(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), SnapshotLabel);

        await NewPipeline(log: log).CopyAsync(source, Allocation(ranges), new MemoryTargetDisk(16 * Mib), 0, new RecordingProgress(), CancellationToken.None);

        ((string)DiskOperationEvents(log, "Read").Single()["Disk"]!).ShouldBe(SnapshotLabel);
        ((string)DiskOperationEvents(log, "Write").Single()["Disk"]!).ShouldBe(MemoryTargetDisk.Label);
    }

    [Fact]
    public async Task AReadFromASourceWithoutALabelUsesThePipelineLabel()
    {
        Extent[] ranges = [new(0, 4 * Mib)];
        var log = new RecordingExecutionLog();

        await NewPipeline(log: log).CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), new MemoryTargetDisk(16 * Mib), 0, new RecordingProgress(), CancellationToken.None);

        ((string)DiskOperationEvents(log, "Read").Single()["Disk"]!).ShouldBe("disk-under-test");
    }

    [Fact]
    public async Task TheReadOffsetIsInsideTheSourceVolumeAndTheWriteOffsetIsOnTheTargetDisk()
    {
        // The first used range starts 1 MiB into the source volume and lands 2 MiB into the target disk.
        Extent[] ranges = [new(1 * Mib, 4096)];
        var log = new RecordingExecutionLog();
        var source = new LabeledBlockSource(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), SnapshotLabel);

        await NewPipeline(log: log).CopyAsync(source, Allocation(ranges), new MemoryTargetDisk(16 * Mib), 2 * Mib, new RecordingProgress(), CancellationToken.None);

        ((long)DiskOperationEvents(log, "Read").Single()["Offset"]!).ShouldBe(1 * Mib);
        ((long)DiskOperationEvents(log, "Write").Single()["Offset"]!).ShouldBe(3 * Mib);
    }

    [Fact]
    public async Task EveryBufferIsReturnedToThePoolAfterASuccessfulCopy()
    {
        Extent[] ranges = [new(0, 12 * Mib)];
        AlignedBufferPool? pool = null;

        // Two buffers for three chunks, so every buffer is returned and rented again.
        await NewPipeline(() => pool = new AlignedBufferPool((nuint)4096, bufferCount: 2)).CopyAsync(
            new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)),
            Allocation(ranges),
            new MemoryTargetDisk(16 * Mib),
            0,
            new RecordingProgress(),
            CancellationToken.None);

        RentedCount(pool!).ShouldBe(0);
    }

    [Fact]
    public async Task EveryBufferIsReturnedToThePoolAfterAFailedCopy()
    {
        Extent[] ranges = [new(0, 12 * Mib)];
        AlignedBufferPool? pool = null;
        var target = new MemoryTargetDisk(16 * Mib, failOnWrite: 1);

        await Should.ThrowAsync<PlatformException>(() => NewPipeline(() => pool = new AlignedBufferPool((nuint)4096, bufferCount: 2)).CopyAsync(
            new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)),
            Allocation(ranges),
            target,
            0,
            new RecordingProgress(),
            CancellationToken.None));

        RentedCount(pool!).ShouldBe(0);
    }

    [Fact]
    public async Task EmptyAllocationCopiesNothing()
    {
        var target = new MemoryTargetDisk(4 * Mib);

        await NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(4 * Mib), Allocation([])), Allocation([]), target, 0, new RecordingProgress(), CancellationToken.None);

        target.Attempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task RangeNotAlignedTo512BytesIsRejectedBeforeAnyWrite()
    {
        Extent[] ranges = [new(100, 4096)];
        var target = new MemoryTargetDisk(16 * Mib);

        await Should.ThrowAsync<ArgumentException>(() =>
            NewPipeline().CopyAsync(new MemoryBlockSource(Pattern(16 * Mib), Allocation(ranges)), Allocation(ranges), target, 0, new RecordingProgress(), CancellationToken.None));

        target.Attempts.ShouldBeEmpty();
    }

    private static CopyPipeline NewPipeline(Func<AlignedBufferPool>? poolFactory = null, RecordingExecutionLog? log = null) =>
        new(
            poolFactory ?? (() => CopyPipeline.CreateDefaultPool()),
            new DiskOperationRecorder(log ?? new RecordingExecutionLog(), new ManualClock(DateTimeOffset.UnixEpoch)),
            "disk-under-test");

    private static VolumeAllocation Allocation(Extent[] ranges) =>
        new(new ByteSize(512), ranges);

    private static byte[] Pattern(long length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 31 + 7) % 251 + 1);
        }

        return data;
    }

    private static List<string> Operations(RecordingExecutionLog log) =>
        log.Events.Where(e => e.Event == "DiskOperation").Select(e => (string)e.Properties["Operation"]!).ToList();

    private static List<IReadOnlyDictionary<string, object?>> DiskOperationEvents(RecordingExecutionLog log, string operation) =>
        log.Events
            .Where(e => e.Event == "DiskOperation" && (string)e.Properties["Operation"]! == operation)
            .Select(e => e.Properties)
            .ToList();

    /// <summary>Counts the buffers the pool still has rented. The pool keeps this state in a private array.</summary>
    private static int RentedCount(AlignedBufferPool pool)
    {
        var rented = (bool[])typeof(AlignedBufferPool)
            .GetField("_rented", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(pool)!;
        return rented.Count(r => r);
    }

    private sealed class RecordingProgress : IProgress<long>
    {
        public List<long> Values { get; } = [];

        public void Report(long value) => Values.Add(value);
    }

    /// <summary>Serves reads from an in-memory array. It fails on the read with the given index, if asked to.</summary>
    private sealed class MemoryBlockSource(byte[] data, VolumeAllocation allocation, int failOnRead = -1, Exception? failure = null) : IBlockSource
    {
        private int _reads;

        public ByteSize Length => new(data.Length);

        public Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct) => Task.FromResult(allocation);

        public ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct)
        {
            int index = _reads++;
            if (index == failOnRead)
            {
                return ValueTask.FromException<int>(failure ?? new PlatformException(PlatformErrorCode.SourceReadError, "injected"));
            }

            data.AsMemory((int)offset, alignedBuffer.Length).CopyTo(alignedBuffer);
            return ValueTask.FromResult(alignedBuffer.Length);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Records every write attempt and stores the bytes of the writes that succeed.</summary>
    private sealed class MemoryTargetDisk(long length, int? failOnWrite = null, Action<int>? onWrite = null, Exception? writeFailure = null)
        : ITargetDisk, IDiskOperationLabel
    {
        public const string Label = "target-under-test";

        public byte[] Image { get; } = new byte[length];

        public string DiskOperationLabel => Label;

        public List<(long Offset, int Length)> Attempts { get; } = [];

        public ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct)
        {
            int index = Attempts.Count;
            Attempts.Add((diskOffset, alignedBuffer.Length));
            onWrite?.Invoke(index);

            if (index == failOnWrite)
            {
                return ValueTask.FromException(writeFailure ?? new IOException("injected write failure"));
            }

            alignedBuffer.Span.CopyTo(Image.AsSpan((int)diskOffset));
            return ValueTask.CompletedTask;
        }

        public Task LockAndTakeOfflineAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task WipeBoundariesAsync(CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task CommitLayoutAsync(WinClonerX.Domain.Planning.TargetLayout layout, CancellationToken ct) => throw new NotSupportedException();

        public Task BringOnlineAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteLayoutAsync(CancellationToken ct) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
