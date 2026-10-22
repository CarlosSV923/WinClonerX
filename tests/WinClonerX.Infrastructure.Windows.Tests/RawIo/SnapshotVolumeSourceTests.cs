using System.ComponentModel;
using System.Runtime.InteropServices;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Snapshots;
using WinClonerX.Infrastructure.Windows.Tests.Snapshots;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// The device is replaced by a fake. No test opens a device.
public sealed class SnapshotVolumeSourceTests
{
    private const int Sector = 512;
    private const int Cluster = 4096;

    // Small volume: 128 clusters, and a 4096-byte tail after them. The backup sector ends the device.
    private const long SmallLength = 528896;

    // Real numbers from spike S3 (BitLocker snapshot).
    private const long S3TotalClusters = 126959;
    private const long S3NumberSectors = 1015679;
    private const long S3Length = 520028160;

    private static NTFS_VOLUME_DATA_BUFFER SmallVolume() => new()
    {
        TotalClusters = 128,
        BytesPerCluster = Cluster,
        NumberSectors = 1032,
        BytesPerSector = Sector,
    };

    private static NTFS_VOLUME_DATA_BUFFER S3Volume() => new()
    {
        TotalClusters = S3TotalClusters,
        BytesPerCluster = Cluster,
        NumberSectors = S3NumberSectors,
        BytesPerSector = Sector,
    };

    /// <summary>A volume of <paramref name="clusters"/> clusters with the same geometry as <see cref="SmallVolume"/>.</summary>
    private static NTFS_VOLUME_DATA_BUFFER VolumeOf(long clusters) => new()
    {
        TotalClusters = clusters,
        BytesPerCluster = Cluster,
        NumberSectors = clusters * (Cluster / Sector),
        BytesPerSector = Sector,
    };

    /// <summary>The device length of <see cref="VolumeOf"/>: the sectors of the volume plus the backup boot sector.</summary>
    private static long LengthOf(long clusters) => (clusters * (Cluster / Sector) + 1) * Sector;

    /// <summary>The output buffer size for a bitmap chunk of <paramref name="clusters"/> clusters, header included.</summary>
    private static int ChunkBytes(int clusters) => VolumeBitmapParser.HeaderBytes + clusters / 8;

    [Fact]
    public void CallsAllowExtendedDasdIoBeforeAnyOtherCall()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength);

        using var source = new SnapshotVolumeSource(device);

        device.Calls.First().ShouldBe("AllowExtendedDasdIo");
    }

    [Fact]
    public void ExposesTheDeviceLength()
    {
        using var source = new SnapshotVolumeSource(new FakeSnapshotDevice(SmallVolume(), SmallLength));

        source.Length.ShouldBe(new ByteSize(SmallLength));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MergesARunThatCrossesTheBitmapChunkBoundary(bool reportsNoByteCountOnMoreData)
    {
        // Chunks of 64 clusters: the run 60..67 starts in the first chunk and ends in the second. The second theory case
        // is a device that reports no byte count when the buffer is full, so the whole buffer is taken as present.
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength) { ReportsNoByteCountOnMoreData = reportsNoByteCountOnMoreData };
        for (long lcn = 60; lcn < 68; lcn++)
        {
            device.UsedClusters.Add(lcn);
        }

        using var source = new SnapshotVolumeSource(device, ChunkBytes(64));

        VolumeAllocation allocation = await source.GetAllocationAsync(CancellationToken.None);

        device.BitmapCalls.ShouldBe(2);
        allocation.ClusterSize.ShouldBe(new ByteSize(Cluster));
        allocation.UsedRanges.ShouldBe([
            new Extent(0, 8192),
            new Extent(60 * Cluster, 8 * Cluster),
            new Extent(128 * Cluster, 4608),
        ]);
    }

    [Fact]
    public async Task ReadsAVolumeThatNeedsManyChunksAndMergesRunsAcrossThem()
    {
        // 1024 clusters in chunks of 16: 64 bitmap calls. The run 100..499 crosses 25 chunks.
        const long clusters = 1024;
        var device = new FakeSnapshotDevice(VolumeOf(clusters), LengthOf(clusters));
        for (long lcn = 100; lcn < 500; lcn++)
        {
            device.UsedClusters.Add(lcn);
        }

        device.UsedClusters.Add(1000);
        device.UsedClusters.Add(clusters - 1);

        using var source = new SnapshotVolumeSource(device, ChunkBytes(16));

        VolumeAllocation allocation = await source.GetAllocationAsync(CancellationToken.None);

        // Each call starts where the previous chunk ended, so no cluster is skipped or read twice.
        device.BitmapRequests.ShouldBe(Enumerable.Range(0, 64).Select(i => (long)i * 16));
        allocation.UsedRanges.ShouldBe([
            new Extent(0, 8192),
            new Extent(100 * Cluster, 400 * Cluster),
            new Extent(1000 * Cluster, Cluster),
            new Extent((clusters - 1) * Cluster, 4608),
        ]);
    }

    [Fact]
    public async Task IncludesTheBootRegionTheTailAndTheBackupBootSectorWithTheS3Geometry()
    {
        // 8192-cluster chunks: 16 bitmap calls for the 126959 clusters of the S3 volume.
        var device = new FakeSnapshotDevice(S3Volume(), S3Length);
        device.UsedClusters.Add(5);
        device.UsedClusters.Add(S3TotalClusters - 1);

        using var source = new SnapshotVolumeSource(device, ChunkBytes(8192));

        VolumeAllocation allocation = await source.GetAllocationAsync(CancellationToken.None);

        // The last cluster, the tail (3584 bytes) and the backup sector form one range that ends at the device length.
        allocation.UsedRanges.ShouldBe([
            new Extent(0, 8192),
            new Extent(5 * Cluster, Cluster),
            new Extent((S3TotalClusters - 1) * Cluster, 8192),
        ]);
    }

    [Fact]
    public async Task CachesTheAllocationSoTheDeviceIsQueriedOnce()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength);
        using var source = new SnapshotVolumeSource(device, ChunkBytes(64));

        VolumeAllocation first = await source.GetAllocationAsync(CancellationToken.None);
        int callsAfterFirst = device.BitmapCalls;
        VolumeAllocation second = await source.GetAllocationAsync(CancellationToken.None);

        second.ShouldBeSameAs(first);
        callsAfterFirst.ShouldBe(2);
        device.BitmapCalls.ShouldBe(callsAfterFirst);
    }

    [Fact]
    public async Task FailsWhenTheBackupBootSectorIsBeyondTheDevice()
    {
        using var source = new SnapshotVolumeSource(new FakeSnapshotDevice(SmallVolume(), SmallLength - 1));

        await Should.ThrowAsync<InvalidOperationException>(() => source.GetAllocationAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ReadsTheRequestedRangeFromTheSnapshot()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength);
        using var source = new SnapshotVolumeSource(device);

        int read = await source.ReadAsync(SmallLength - 4096, new byte[4096], CancellationToken.None);

        read.ShouldBe(4096);
        device.Reads.ShouldBe([(SmallLength - 4096, 4096)]);
    }

    [Fact]
    public async Task ShortReadIsASourceReadError()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength) { ShortReadBy = 1 };
        using var source = new SnapshotVolumeSource(device);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            async () => await source.ReadAsync(0, new byte[4096], CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
    }

    [Fact]
    public async Task DeviceFailureIsASourceReadErrorWithTheCause()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength) { ReadFailure = new Win32Exception(23, "device error") };
        using var source = new SnapshotVolumeSource(device);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            async () => await source.ReadAsync(0, new byte[4096], CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
        ex.InnerException.ShouldBeOfType<Win32Exception>();
    }

    [Fact]
    public async Task RejectsAMisalignedOffset()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength);
        using var source = new SnapshotVolumeSource(device);

        await Should.ThrowAsync<ArgumentException>(
            async () => await source.ReadAsync(100, new byte[4096], CancellationToken.None));

        device.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task RejectsARequestThatEndsBeyondTheLength()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength);
        using var source = new SnapshotVolumeSource(device);

        await Should.ThrowAsync<ArgumentOutOfRangeException>(
            async () => await source.ReadAsync(SmallLength, new byte[Sector], CancellationToken.None));

        device.Reads.ShouldBeEmpty();
    }

    [Fact]
    public void DisposeClosesTheDeviceOnlyOnce()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength);
        var source = new SnapshotVolumeSource(device);

        source.Dispose();
        source.Dispose();

        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void ConstructorDisposesTheDeviceWhenTheGeometryCannotBeRead()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength) { DasdFailure = new Win32Exception(87, "invalid") };

        PlatformException ex = Should.Throw<PlatformException>(() => new SnapshotVolumeSource(device));

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void ConstructorRejectsAChunkThatCannotHoldTheHeaderAndDisposesTheDevice()
    {
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength);

        Should.Throw<ArgumentOutOfRangeException>(() => new SnapshotVolumeSource(device, VolumeBitmapParser.HeaderBytes));

        device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public void OpenVolumeRejectsAPartitionWithoutASnapshot()
    {
        var set = new VssSnapshotSet(
            Guid.NewGuid(),
            new Dictionary<PartitionGuid, string>(),
            new FakeVssSession(),
            new MtaWorker(),
            onWarning: null);

        Should.Throw<KeyNotFoundException>(() => set.OpenVolume(new PartitionGuid(Guid.NewGuid())));
    }
}

/// <summary>
/// Fake <see cref="ISnapshotDevice"/> over a volume with a bitmap of <see cref="UsedClusters"/>. It records each call in
/// order. Each bitmap call builds the raw FSCTL_GET_VOLUME_BITMAP output that Windows would produce for the buffer it
/// is given, and runs it through <see cref="VolumeBitmapParser"/>, so the fake follows the device contract and not the
/// parser's own output.
/// </summary>
internal sealed class FakeSnapshotDevice(NTFS_VOLUME_DATA_BUFFER volume, long length) : ISnapshotDevice
{
    public List<string> Calls { get; } = [];

    public HashSet<long> UsedClusters { get; } = [];

    /// <summary>The starting cluster of each bitmap call, in order.</summary>
    public List<long> BitmapRequests { get; } = [];

    /// <summary>
    /// When true, a call that fails with ERROR_MORE_DATA reports 0 bytes, as a driver may do if it does not set
    /// lpBytesReturned on overflow.
    /// </summary>
    public bool ReportsNoByteCountOnMoreData { get; set; }

    /// <summary>Bytes missing from each read, which makes the read short.</summary>
    public int ShortReadBy { get; set; }

    public Exception? DasdFailure { get; set; }

    public Exception? ReadFailure { get; set; }

    public List<(long Offset, int Length)> Reads { get; } = [];

    public int DisposeCount { get; private set; }

    public int BitmapCalls => Calls.Count(call => call == "ReadBitmapChunk");

    public void AllowExtendedDasdIo()
    {
        Calls.Add("AllowExtendedDasdIo");
        if (DasdFailure is not null)
        {
            throw DasdFailure;
        }
    }

    public long GetLength()
    {
        Calls.Add("GetLength");
        return length;
    }

    public NTFS_VOLUME_DATA_BUFFER GetNtfsVolumeData()
    {
        Calls.Add("GetNtfsVolumeData");
        return volume;
    }

    public int ReadBitmapChunk(
        long startingLcn, Span<byte> output, out long chunkStartingLcn, out long chunkClusters, out bool moreData)
    {
        Calls.Add("ReadBitmapChunk");
        BitmapRequests.Add(startingLcn);

        // As FSCTL_GET_VOLUME_BITMAP does: the start is rounded down to a multiple of 8, and BitmapSize counts the clusters
        // from that start to the end of the volume. A buffer that cannot hold the rest fails with ERROR_MORE_DATA and
        // holds the bitmap bytes that fit after the header.
        long start = startingLcn & ~7L;
        long bitmapSize = volume.TotalClusters - start;
        long neededBytes = (bitmapSize + 7) / 8;
        long presentBytes = Math.Min(neededBytes, output.Length - VolumeBitmapParser.HeaderBytes);
        moreData = presentBytes < neededBytes;

        var header = new VOLUME_BITMAP_BUFFER { StartingLcn = start, BitmapSize = bitmapSize };
        MemoryMarshal.Write(output, in header);

        Span<byte> bits = output.Slice(VolumeBitmapParser.HeaderBytes, (int)presentBytes);
        bits.Clear();
        for (long bit = 0; bit < presentBytes * 8 && start + bit < volume.TotalClusters; bit++)
        {
            if (UsedClusters.Contains(start + bit))
            {
                bits[(int)(bit >> 3)] |= (byte)(1 << (int)(bit & 7));
            }
        }

        uint returned = ReportsNoByteCountOnMoreData && moreData
            ? 0
            : (uint)(VolumeBitmapParser.HeaderBytes + presentBytes);
        return VolumeBitmapParser.Parse(output, returned, moreData, out chunkStartingLcn, out chunkClusters);
    }

    public int Read(long offset, Span<byte> alignedDestination)
    {
        Calls.Add("Read");
        Reads.Add((offset, alignedDestination.Length));
        if (ReadFailure is not null)
        {
            throw ReadFailure;
        }

        int read = Math.Max(0, alignedDestination.Length - ShortReadBy);
        alignedDestination[..read].Fill(0xAB);
        return read;
    }

    public void Dispose()
    {
        DisposeCount++;
    }
}
