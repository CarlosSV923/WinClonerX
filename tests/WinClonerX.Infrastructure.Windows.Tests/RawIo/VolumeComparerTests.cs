using System.ComponentModel;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Logging;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Tests.Fakes;
using WinClonerX.Infrastructure.Windows.Tests.Logging;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// Source and target are in memory. Differences and failures are injected, so every case is deterministic. The chunk is
// small (4 KiB) so that ranges cross chunk boundaries without large buffers.
public sealed class VolumeComparerTests
{
    private const int Chunk = 4096;
    private const long TargetBase = 1024 * 1024;

    [Fact]
    public async Task ComparisonReadsRunOffTheCallingThread()
    {
        byte[] source = Pattern(Chunk);
        Extent[] ranges = [new(0, Chunk)];
        int callerThreadId = 0;
        int sourceReadThreadId = 0;
        int targetReadThreadId = 0;
        Task<RangeComparison>? comparison = null;

        var caller = new Thread(() =>
        {
            callerThreadId = Environment.CurrentManagedThreadId;
            comparison = NewComparer().CompareAsync(
                new MemoryBlockSource(source, onRead: () => sourceReadThreadId = Environment.CurrentManagedThreadId),
                Allocation(ranges),
                new MemoryTarget(CopiedImage(source, ranges), onRead: () => targetReadThreadId = Environment.CurrentManagedThreadId),
                TargetBase,
                new RecordingProgress(),
                CancellationToken.None);
        });

        caller.Start();
        caller.Join();
        (await comparison!).BytesCompared.ShouldBe(Chunk);

        sourceReadThreadId.ShouldNotBe(callerThreadId);
        targetReadThreadId.ShouldNotBe(callerThreadId);
    }

    [Fact]
    public async Task IdenticalRangesCompareEqualAndLogOneReadOfTheTarget()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 8192), new(16384, 4096)];
        var log = new RecordingExecutionLog();

        RangeComparison result = await NewComparer(log).CompareAsync(
            new MemoryBlockSource(source), Allocation(ranges), new MemoryTarget(CopiedImage(source, ranges)), TargetBase,
            new RecordingProgress(), CancellationToken.None);

        result.BytesCompared.ShouldBe(12288);
        result.DifferingBytes.ShouldBe(0);
        result.FirstDifferenceOffsets.ShouldBeEmpty();

        LogEvent read = log.Events.Single();
        read.Event.ShouldBe("DiskOperation");
        read.Properties["Operation"].ShouldBe("Read");
        read.Properties["Disk"].ShouldBe("target");
        read.Properties["Offset"].ShouldBe(TargetBase);
        read.Properties["Length"].ShouldBe(12288L);
        read.Properties["Result"].ShouldBe("Success");
    }

    [Fact]
    public async Task ADifferenceIsCountedAndItsOffsetIsKeptInSourceCoordinates()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(4096, 8192)];
        byte[] image = CopiedImage(source, ranges);
        image[TargetBase + 4096 + 5] ^= 0xFF;

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation(ranges), new MemoryTarget(image), TargetBase, new RecordingProgress(), CancellationToken.None);

        result.DifferingBytes.ShouldBe(1);
        result.FirstDifferenceOffsets.ShouldBe([4096L + 5]);
    }

    [Fact]
    public async Task BytesOutsideTheUsedRangesAreNotCompared()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 4096)];
        byte[] image = CopiedImage(source, ranges);
        image[TargetBase + 8192] ^= 0xFF;

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation(ranges), new MemoryTarget(image), TargetBase, new RecordingProgress(), CancellationToken.None);

        result.DifferingBytes.ShouldBe(0);
        result.BytesCompared.ShouldBe(4096);
    }

    [Fact]
    public async Task DifferencesAcrossChunkBoundariesAreAllCountedInOrder()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 3 * Chunk)];
        byte[] image = CopiedImage(source, ranges);
        image[TargetBase + 10] ^= 0xFF;                // first chunk
        image[TargetBase + Chunk + Chunk - 1] ^= 0xFF; // last byte of the second chunk
        image[TargetBase + 2 * Chunk] ^= 0xFF;         // first byte of the third chunk

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation(ranges), new MemoryTarget(image), TargetBase, new RecordingProgress(), CancellationToken.None);

        result.DifferingBytes.ShouldBe(3);
        result.FirstDifferenceOffsets.ShouldBe([10L, 2L * Chunk - 1, 2L * Chunk]);
    }

    [Fact]
    public async Task EveryDifferenceIsCountedButOnlyTheFirstTenOffsetsAreKept()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 16384)];
        byte[] image = CopiedImage(source, ranges);
        for (int i = 0; i < 25; i++)
        {
            image[TargetBase + i] ^= 0xFF;
        }

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation(ranges), new MemoryTarget(image), TargetBase, new RecordingProgress(), CancellationToken.None);

        result.DifferingBytes.ShouldBe(25);
        result.FirstDifferenceOffsets.ShouldBe(Enumerable.Range(0, VolumeComparer.MaxRecordedOffsets).Select(i => (long)i).ToArray());
    }

    [Fact]
    public async Task MismatchSampleReportsAnUnstableTargetRead()
    {
        byte[] source = Pattern(64 * 1024);
        int reads = 0;
        var target = new MemoryTarget(new byte[TargetBase + source.Length], readOverride: (read, _, buffer) =>
        {
            source.AsMemory(0, buffer.Length).CopyTo(buffer);
            if (read == 0)
            {
                buffer.Span[0] ^= 0xFF;
            }

            reads++;
            return buffer.Length;
        });

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation([new(0, Chunk)]), target, TargetBase,
            new RecordingProgress(), CancellationToken.None);

        result.DifferingBytes.ShouldBe(1);
        var sample = result.MismatchSamples!.Single();
        sample.TargetStable.ShouldBeFalse();
        sample.SourceStable.ShouldBeTrue();
        reads.ShouldBe(2);
    }

    [Fact]
    public async Task MismatchSampleReportsAStableWrongTargetAndSecondReadDifferences()
    {
        byte[] source = Pattern(64 * 1024);
        byte[] image = CopiedImage(source, [new(0, Chunk)]);
        image[TargetBase + 12] ^= 0xFF;

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation([new(0, Chunk)]), new MemoryTarget(image), TargetBase,
            new RecordingProgress(), CancellationToken.None);

        var sample = result.MismatchSamples!.Single();
        sample.TargetStable.ShouldBeTrue();
        sample.SecondReadDiffering.ShouldBe(1);
    }

    [Fact]
    public async Task MismatchSampleReportsZeroFilledTargetBytes()
    {
        byte[] source = Pattern(64 * 1024);

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation([new(0, Chunk)]), new MemoryTarget(new byte[TargetBase + source.Length]),
            TargetBase, new RecordingProgress(), CancellationToken.None);

        var sample = result.MismatchSamples!.Single();
        sample.TargetAllZero.ShouldBeTrue();
        sample.SourceAllZero.ShouldBeFalse();
    }

    [Fact]
    public async Task MismatchSampleFindsA4096ByteShift()
    {
        const int testChunk = 128 * 1024;
        byte[] source = Pattern(256 * 1024);
        new Random(42).NextBytes(source);
        byte[] image = new byte[TargetBase + source.Length];
        source.AsSpan(4096, testChunk).CopyTo(image.AsSpan((int)TargetBase, testChunk));

        RangeComparison result = await new VolumeComparer(
                new DiskOperationRecorder(new RecordingExecutionLog(), new ManualClock(DateTimeOffset.UnixEpoch)), "target", testChunk)
            .CompareAsync(new MemoryBlockSource(source), Allocation([new(0, testChunk)]), new MemoryTarget(image), TargetBase,
                new RecordingProgress(), CancellationToken.None);

        result.MismatchSamples!.Single().Shift.ShouldBe(4096L);
    }

    [Fact]
    public async Task AtMostThreeMismatchSamplesAreCreatedPerComparison()
    {
        byte[] source = Pattern(64 * 1024);
        byte[] image = CopiedImage(source, [new(0, 5 * Chunk)]);
        for (int i = 0; i < 5 * Chunk; i += Chunk)
        {
            image[TargetBase + i] ^= 0xFF;
        }

        RangeComparison result = await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation([new(0, 5 * Chunk)]), new MemoryTarget(image), TargetBase,
            new RecordingProgress(), CancellationToken.None);

        result.MismatchSamples!.Count.ShouldBe(3);
    }

    [Fact]
    public async Task ProgressIsCumulativeAndEndsAtTheTotal()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 3 * Chunk + 512)];
        var progress = new RecordingProgress();

        await NewComparer().CompareAsync(
            new MemoryBlockSource(source), Allocation(ranges), new MemoryTarget(CopiedImage(source, ranges)), TargetBase, progress, CancellationToken.None);

        progress.Values.ShouldBe([4096L, 8192L, 12288L, 12800L]);
    }

    [Fact]
    public async Task ASourceReadErrorIsASourceReadErrorAndLogsAFailedRead()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 8192)];
        var log = new RecordingExecutionLog();

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() => NewComparer(log).CompareAsync(
            new MemoryBlockSource(source, new IOException("bad sector")),
            Allocation(ranges),
            new MemoryTarget(CopiedImage(source, ranges)),
            TargetBase,
            new RecordingProgress(),
            CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
        log.Events.Single().Properties["Result"].ShouldBe("Failed");
        log.Events.Single().Level.ShouldBe(LogLevelKind.Error);
    }

    [Fact]
    public async Task ASourceDeviceThatDisappearsIsASourceDeviceRemoval()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 4096)];

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() => NewComparer().CompareAsync(
            new MemoryBlockSource(source, new Win32Exception(1167)),
            Allocation(ranges),
            new MemoryTarget(CopiedImage(source, ranges)),
            TargetBase,
            new RecordingProgress(),
            CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.SourceDeviceRemoved);
    }

    [Fact]
    public async Task ATargetDeviceThatDisappearsIsATargetDeviceRemovalAndLogsAFailedRead()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 4096)];
        var log = new RecordingExecutionLog();

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() => NewComparer(log).CompareAsync(
            new MemoryBlockSource(source),
            Allocation(ranges),
            new MemoryTarget(CopiedImage(source, ranges), new Win32Exception(1167)),
            TargetBase,
            new RecordingProgress(),
            CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.TargetDeviceRemoved);
        log.Events.Single().Properties["Result"].ShouldBe("Failed");
    }

    [Fact]
    public async Task AnUnalignedRangeIsRejectedBeforeAnyRead()
    {
        byte[] source = Pattern(64 * 1024);
        var log = new RecordingExecutionLog();

        await Should.ThrowAsync<ArgumentException>(() => NewComparer(log).CompareAsync(
            new MemoryBlockSource(source),
            Allocation([new(100, 4096)]),
            new MemoryTarget(new byte[TargetBase + 8192]),
            TargetBase,
            new RecordingProgress(),
            CancellationToken.None));

        log.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task AnAllocationWithoutRangesComparesNothingAndLogsNothing()
    {
        var log = new RecordingExecutionLog();

        RangeComparison result = await NewComparer(log).CompareAsync(
            new MemoryBlockSource(Pattern(4096)),
            Allocation([]),
            new MemoryTarget(new byte[TargetBase]),
            TargetBase,
            new RecordingProgress(),
            CancellationToken.None);

        result.BytesCompared.ShouldBe(0);
        result.DifferingBytes.ShouldBe(0);
        log.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancellationBetweenChunksStopsTheComparison()
    {
        byte[] source = Pattern(64 * 1024);
        Extent[] ranges = [new(0, 8 * Chunk)];
        using var cts = new CancellationTokenSource();

        await Should.ThrowAsync<OperationCanceledException>(() => NewComparer().CompareAsync(
            new MemoryBlockSource(source),
            Allocation(ranges),
            new MemoryTarget(CopiedImage(source, ranges)),
            TargetBase,
            new CancelOnFirstReport(cts),
            cts.Token));
    }

    private static VolumeComparer NewComparer(RecordingExecutionLog? log = null, int chunkBytes = Chunk) =>
        new(new DiskOperationRecorder(log ?? new RecordingExecutionLog(), new ManualClock(DateTimeOffset.UnixEpoch)), "target", chunkBytes);

    private static VolumeAllocation Allocation(Extent[] ranges) => new(new ByteSize(512), ranges);

    /// <summary>A target image in which every range of the source has been copied to <see cref="TargetBase"/> plus its offset.</summary>
    private static byte[] CopiedImage(byte[] source, Extent[] ranges)
    {
        byte[] image = new byte[TargetBase + source.Length];
        foreach (Extent range in ranges)
        {
            source.AsSpan((int)range.Offset, (int)range.Length).CopyTo(image.AsSpan((int)(TargetBase + range.Offset)));
        }

        return image;
    }

    private static byte[] Pattern(long length)
    {
        byte[] data = new byte[length];
        for (int i = 0; i < data.Length; i++)
        {
            data[i] = (byte)((i * 31 + 7) % 251 + 1);
        }

        return data;
    }

    private sealed class RecordingProgress : IProgress<long>
    {
        public List<long> Values { get; } = [];

        public void Report(long value) => Values.Add(value);
    }

    /// <summary>Requests a cancellation on the first progress report, so the next chunk must stop.</summary>
    private sealed class CancelOnFirstReport(CancellationTokenSource cts) : IProgress<long>
    {
        public void Report(long value) => cts.Cancel();
    }

    private sealed class MemoryBlockSource(byte[] data, Exception? failure = null, Action? onRead = null) : IBlockSource
    {
        public ByteSize Length => new(data.Length);

        public Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct) => throw new NotSupportedException();

        public ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct)
        {
            onRead?.Invoke();
            if (failure is not null)
            {
                return new ValueTask<int>(Task.FromException<int>(failure));
            }

            int count = (int)Math.Min(alignedBuffer.Length, data.Length - offset);
            data.AsMemory((int)offset, count).CopyTo(alignedBuffer);
            return ValueTask.FromResult(count);
        }

        public void Dispose()
        {
        }
    }

    /// <summary>Reads from an in-memory image of the target disk. Only the read is supported.</summary>
    private sealed class MemoryTarget(
        byte[] image,
        Exception? readFailure = null,
        Func<int, long, Memory<byte>, int>? readOverride = null,
        Action? onRead = null) : ITargetDisk
    {
        private int _readCount;

        public ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct)
        {
            onRead?.Invoke();
            if (readFailure is not null)
            {
                return new ValueTask<int>(Task.FromException<int>(readFailure));
            }

            if (readOverride is not null)
            {
                return ValueTask.FromResult(readOverride(_readCount++, diskOffset, alignedBuffer));
            }

            int count = (int)Math.Min(alignedBuffer.Length, image.Length - diskOffset);
            image.AsMemory((int)diskOffset, count).CopyTo(alignedBuffer);
            return ValueTask.FromResult(count);
        }

        public Task LockAndTakeOfflineAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task WipeBoundariesAsync(CancellationToken ct) => throw new NotSupportedException();

        public ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task CommitLayoutAsync(TargetLayout layout, CancellationToken ct) => throw new NotSupportedException();

        public Task BringOnlineAsync(CancellationToken ct) => throw new NotSupportedException();

        public Task DeleteLayoutAsync(CancellationToken ct) => throw new NotSupportedException();

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
