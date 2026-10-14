using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.RawIo;
using Shouldly;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.RawIo;

public sealed class VolumeAllocationPlannerTests
{
    // Numbers from spike S3: a 520028160-byte partition, 512-byte sectors, 4096-byte clusters.
    private const long S3DeviceBytes = 520028160;
    private const long S3Sector = 512;
    private const long S3Cluster = 4096;
    private const long S3TotalClusters = 126959;
    private const long S3NumberSectors = S3DeviceBytes / S3Sector - 1;

    // A small synthetic volume that ends exactly on a cluster boundary (no tail).
    private const long SmallSector = 512;
    private const long SmallCluster = 4096;
    private const long SmallTotalClusters = 100;
    private const long SmallNumberSectors = 800;
    private const long SmallDeviceBytes = SmallNumberSectors * SmallSector + SmallSector; // includes the backup sector

    // ---------- UsedRuns: bitmap decoding ----------

    [Fact]
    public void UsedRunsLowestBitIsStartingLcn()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0x01 }, 0, 8).ShouldEqualItems(new[] { (0L, 1L) });
    }

    [Fact]
    public void UsedRunsHighestBitOfFirstByteIsSeventhLcn()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0x80 }, 0, 8).ShouldEqualItems(new[] { (7L, 1L) });
    }

    [Fact]
    public void UsedRunsBitsOfSecondByteUseLsbFirstOrder()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0x00, 0x02 }, 0, 16).ShouldEqualItems(new[] { (9L, 1L) });
    }

    [Fact]
    public void UsedRunsRunCrossingByteBoundaryIsMerged()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0x80, 0x01 }, 0, 16).ShouldEqualItems(new[] { (7L, 2L) });
    }

    [Fact]
    public void UsedRunsStartingLcnOffsetsEveryBit()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0x05 }, 100, 200).ShouldEqualItems(new[] { (100L, 1L), (102L, 1L) });
    }

    [Fact]
    public void UsedRunsAlternatingBitsGiveSeparateRuns()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0x55 }, 0, 8)
            .ShouldEqualItems(new[] { (0L, 1L), (2L, 1L), (4L, 1L), (6L, 1L) });
    }

    [Fact]
    public void UsedRunsBitsAtOrAboveTotalClustersAreIgnored()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0xFF }, 0, 5).ShouldEqualItems(new[] { (0L, 5L) });
    }

    [Fact]
    public void UsedRunsWhenTotalClustersIsBelowStartingLcnNothingIsUsed()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0xFF }, 10, 5).ShouldBeEmpty();
    }

    [Fact]
    public void UsedRunsAllZeroBitmapHasNoRuns()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[4], 0, 32).ShouldBeEmpty();
    }

    [Fact]
    public void UsedRunsAllOneBitmapIsOneRun()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0xFF, 0xFF }, 0, 16).ShouldEqualItems(new[] { (0L, 16L) });
    }

    // ---------- UsedRuns: merging ----------

    [Fact]
    public void UsedRunsRunsSpanningSeveralBytesAreMergedIntoOne()
    {
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0xFF, 0xFF, 0x0F }, 0, 100).ShouldEqualItems(new[] { (0L, 20L) });
    }

    [Fact]
    public void UsedRunsGapSplitsRuns()
    {
        // Bits 0-2 used, 3-4 free, 5-7 used.
        VolumeAllocationPlanner.UsedRuns(new byte[] { 0b1110_0111 }, 0, 8).ShouldEqualItems(new[] { (0L, 3L), (5L, 3L) });
    }

    // ---------- Tail and backup placement ----------

    [Fact]
    public void TailAndBackupS3NumbersGiveTailOf3584AndBackupAt520027648()
    {
        VolumeTailLayout layout = VolumeAllocationPlanner.ComputeTailAndBackup(
            S3TotalClusters, S3Cluster, S3NumberSectors, S3Sector, S3DeviceBytes);

        layout.TailOffset.ShouldBe(S3TotalClusters * S3Cluster);
        layout.TailOffset.ShouldBe(520024064);
        layout.TailLength.ShouldBe(3584);
        layout.BackupOffset.ShouldBe(520027648);
    }

    [Fact]
    public void TailAndBackupDeviceShorterThanBackupThrows()
    {
        // The backup sector ends at 520028160; a device of 520027648 bytes cannot supply it.
        var ex = Assert.Throws<InvalidOperationException>(() => VolumeAllocationPlanner.ComputeTailAndBackup(
            S3TotalClusters, S3Cluster, S3NumberSectors, S3Sector, 520027648));

        ex.Message.ShouldContain("FSCTL_ALLOW_EXTENDED_DASD_IO");
    }

    [Fact]
    public void TailAndBackupVolumeEndingOnClusterBoundaryHasNoTail()
    {
        VolumeTailLayout layout = VolumeAllocationPlanner.ComputeTailAndBackup(
            SmallTotalClusters, SmallCluster, SmallNumberSectors, SmallSector, SmallDeviceBytes);

        layout.TailLength.ShouldBe(0);
        layout.BackupOffset.ShouldBe(409600);
    }

    // ---------- Plan ----------

    [Fact]
    public void PlanS3NumbersTailAndBackupAreMergedIntoOneExtent()
    {
        var runs = new[] { (10L, 5L) };

        VolumeAllocation allocation = VolumeAllocationPlanner.Plan(
            runs, S3Cluster, S3TotalClusters, S3NumberSectors, S3Sector, S3DeviceBytes);

        allocation.ClusterSize.ShouldBe(new ByteSize(4096));
        allocation.UsedRanges.ShouldEqualItems(new[]
        {
            new Extent(0, 8192),                 // boot region
            new Extent(40960, 20480),            // run at LCN 10, 5 clusters
            new Extent(520024064, 4096),         // 3584-byte tail + 512-byte backup boot sector, adjacent
        });
    }

    [Fact]
    public void PlanBootRegionMergesWithRunAtLcnZero()
    {
        var runs = new[] { (0L, 2L) };

        VolumeAllocation allocation = VolumeAllocationPlanner.Plan(
            runs, SmallCluster, SmallTotalClusters, SmallNumberSectors, SmallSector, SmallDeviceBytes);

        allocation.UsedRanges.ShouldEqualItems(new[] { new Extent(0, 8192), new Extent(409600, 512) });
    }

    [Fact]
    public void PlanRunInsideBootRegionIsAbsorbed()
    {
        var runs = new[] { (1L, 1L) };

        VolumeAllocation allocation = VolumeAllocationPlanner.Plan(
            runs, SmallCluster, SmallTotalClusters, SmallNumberSectors, SmallSector, SmallDeviceBytes);

        allocation.UsedRanges.ShouldEqualItems(new[] { new Extent(0, 8192), new Extent(409600, 512) });
    }

    [Fact]
    public void PlanAdjacentRunsAreMerged()
    {
        var runs = new[] { (2L, 1L), (3L, 2L) };

        VolumeAllocation allocation = VolumeAllocationPlanner.Plan(
            runs, SmallCluster, SmallTotalClusters, SmallNumberSectors, SmallSector, SmallDeviceBytes);

        allocation.UsedRanges.ShouldEqualItems(new[] { new Extent(0, 20480), new Extent(409600, 512) });
    }

    [Fact]
    public void PlanNoRunsStillCopiesBootRegionAndBackup()
    {
        VolumeAllocation allocation = VolumeAllocationPlanner.Plan(
            Array.Empty<(long StartLcn, long Count)>(), SmallCluster, SmallTotalClusters, SmallNumberSectors,
            SmallSector, SmallDeviceBytes);

        allocation.UsedRanges.ShouldEqualItems(new[] { new Extent(0, 8192), new Extent(409600, 512) });
    }

    [Fact]
    public void PlanTotalUsedEqualsSumOfRangeLengths()
    {
        var runs = new[] { (10L, 5L), (40L, 3L) };

        VolumeAllocation allocation = VolumeAllocationPlanner.Plan(
            runs, S3Cluster, S3TotalClusters, S3NumberSectors, S3Sector, S3DeviceBytes);

        allocation.TotalUsed.Bytes.ShouldBe(allocation.UsedRanges.Sum(range => range.Length));
        allocation.TotalUsed.Bytes.ShouldBe(8192 + 20480 + 12288 + 4096);
    }

    [Fact]
    public void PlanRangesAreSortedNonOverlappingAndSectorAligned()
    {
        var runs = new[] { (0L, 1L), (5L, 2L), (60L, 10L) };

        VolumeAllocation allocation = VolumeAllocationPlanner.Plan(
            runs, S3Cluster, S3TotalClusters, S3NumberSectors, S3Sector, S3DeviceBytes);

        for (int i = 0; i < allocation.UsedRanges.Count; i++)
        {
            Extent range = allocation.UsedRanges[i];
            (range.Offset % S3Sector).ShouldBe(0L);
            (range.Length % S3Sector).ShouldBe(0L);
            if (i > 0)
            {
                allocation.UsedRanges[i - 1].End.ShouldBeLessThan(range.Offset);
            }
        }
    }

    [Fact]
    public void PlanDeviceShorterThanBackupThrowsWithClearMessage()
    {
        var runs = new[] { (10L, 5L) };

        var ex = Assert.Throws<InvalidOperationException>(() => VolumeAllocationPlanner.Plan(
            runs, S3Cluster, S3TotalClusters, S3NumberSectors, S3Sector, 520027648));

        ex.Message.ShouldContain("backup boot sector beyond source device");
    }

    [Fact]
    public void PlanRunOutsideTotalClustersIsRejected()
    {
        var runs = new[] { (SmallTotalClusters - 1, 2L) };

        Assert.Throws<ArgumentException>(() => VolumeAllocationPlanner.Plan(
            runs, SmallCluster, SmallTotalClusters, SmallNumberSectors, SmallSector, SmallDeviceBytes));
    }

    [Fact]
    public void PlanClusterNotMultipleOfSectorIsRejected()
    {
        Assert.Throws<ArgumentException>(() => VolumeAllocationPlanner.Plan(
            Array.Empty<(long StartLcn, long Count)>(), 4000, SmallTotalClusters, SmallNumberSectors, SmallSector,
            SmallDeviceBytes));
    }
}

/// <summary>Element-wise comparison for sequences. Shouldly's generic ShouldBe compares arrays and lists by reference.</summary>
internal static class SequenceAssertions
{
    public static void ShouldEqualItems<T>(this IEnumerable<T> actual, IEnumerable<T> expected)
    {
        Assert.Equal(expected.ToArray(), actual.ToArray());
    }
}
