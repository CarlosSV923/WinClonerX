using System.ComponentModel;
using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;
using WinCopy.Infrastructure.Windows.Snapshots;
using WinCopy.Infrastructure.Windows.Tests.Snapshots;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.RawIo;

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

    [Fact]
    public async Task MergesARunThatCrossesTheBitmapChunkBoundary()
    {
        // Chunks of 64 clusters: the run 60..67 starts in the first chunk and ends in the second.
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength) { ChunkClusters = 64 };
        for (long lcn = 60; lcn < 68; lcn++)
        {
            device.UsedClusters.Add(lcn);
        }

        using var source = new SnapshotVolumeSource(device);

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
    public async Task IncludesTheBootRegionTheTailAndTheBackupBootSectorWithTheS3Geometry()
    {
        var device = new FakeSnapshotDevice(S3Volume(), S3Length) { ChunkClusters = 8192 };
        device.UsedClusters.Add(5);
        device.UsedClusters.Add(S3TotalClusters - 1);

        using var source = new SnapshotVolumeSource(device);

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
        var device = new FakeSnapshotDevice(SmallVolume(), SmallLength) { ChunkClusters = 64 };
        using var source = new SnapshotVolumeSource(device);

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
/// order. The bitmap is returned in chunks of <see cref="ChunkClusters"/> clusters, as FSCTL_GET_VOLUME_BITMAP does.
/// </summary>
internal sealed class FakeSnapshotDevice(NTFS_VOLUME_DATA_BUFFER volume, long length) : ISnapshotDevice
{
    public List<string> Calls { get; } = [];

    public HashSet<long> UsedClusters { get; } = [];

    public int ChunkClusters { get; set; } = 64;

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
        long startingLcn, Span<byte> output, out long chunkStartingLcn, out long bitmapSizeClusters, out bool moreData)
    {
        Calls.Add("ReadBitmapChunk");

        long clusters = Math.Min(ChunkClusters, volume.TotalClusters - startingLcn);
        int bytes = (int)((clusters + 7) / 8);
        output[..bytes].Clear();
        for (int bit = 0; bit < clusters; bit++)
        {
            if (UsedClusters.Contains(startingLcn + bit))
            {
                output[bit >> 3] |= (byte)(1 << (bit & 7));
            }
        }

        chunkStartingLcn = startingLcn;
        bitmapSizeClusters = clusters;
        moreData = startingLcn + clusters < volume.TotalClusters;
        return bytes;
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
