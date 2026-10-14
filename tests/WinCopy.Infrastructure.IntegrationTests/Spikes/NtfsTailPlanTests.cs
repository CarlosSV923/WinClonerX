using Shouldly;
using Xunit;

namespace WinCopy.Infrastructure.IntegrationTests.Spikes;

// Pure unit test for spike S3. It touches no disk and needs no elevation. It is not an integration test.
[Trait("Spike", "S3")]
public sealed class NtfsTailPlanTests
{
    // Numbers from the failing admin run: a 520028160-byte partition, 512-byte sectors, 4096-byte clusters.
    private const long PartitionBytes = 520028160;
    private const long BytesPerSector = 512;
    private const long BytesPerCluster = 4096;
    private const long TotalClusters = 126959;
    private const long NumberSectors = PartitionBytes / BytesPerSector - 1;

    [Fact]
    public void RunNumbersWithSnapshotEndingAtLastClusterHaveNoTailAndSynthesizeBackup()
    {
        long sourceLength = TotalClusters * BytesPerCluster;

        NtfsTailPlan plan = NtfsTailPlan.Plan(
            TotalClusters, BytesPerCluster, NumberSectors, BytesPerSector, sourceLength, PartitionBytes);

        plan.TailOffset.ShouldBe(TotalClusters * BytesPerCluster);
        plan.TailLength.ShouldBe(0);
        plan.BackupOffset.ShouldBe(NumberSectors * BytesPerSector);
        plan.BackupFromSource.ShouldBeFalse();
    }

    [Fact]
    public void FullSizeSnapshotCopiesTailUpToVolumeEndAndReadsBackupFromSource()
    {
        NtfsTailPlan plan = NtfsTailPlan.Plan(
            TotalClusters, BytesPerCluster, NumberSectors, BytesPerSector, PartitionBytes, PartitionBytes);

        plan.TailOffset.ShouldBe(TotalClusters * BytesPerCluster);
        plan.TailLength.ShouldBe(NumberSectors * BytesPerSector - TotalClusters * BytesPerCluster);
        plan.TailLength.ShouldBe(3584);
        (plan.TailLength % BytesPerSector).ShouldBe(0);
        plan.BackupOffset.ShouldBe(NumberSectors * BytesPerSector);
        plan.BackupFromSource.ShouldBeTrue();
    }

    [Fact]
    public void SnapshotEndingInsideTailRoundsTailDownToWholeSectors()
    {
        // Ends 1000 bytes into the tail: one full sector (512) is copied, the partial sector is not.
        long sourceLength = TotalClusters * BytesPerCluster + 1000;

        NtfsTailPlan plan = NtfsTailPlan.Plan(
            TotalClusters, BytesPerCluster, NumberSectors, BytesPerSector, sourceLength, PartitionBytes);

        plan.TailLength.ShouldBe(512);
        plan.BackupFromSource.ShouldBeFalse();
    }

    [Fact]
    public void BackupEndingExactlyAtPartitionEndIsAccepted()
    {
        NtfsTailPlan plan = NtfsTailPlan.Plan(
            TotalClusters, BytesPerCluster, NumberSectors, BytesPerSector, PartitionBytes, PartitionBytes);

        (plan.BackupOffset + BytesPerSector).ShouldBe(PartitionBytes);
    }

    [Fact]
    public void BackupBeyondTargetPartitionThrows()
    {
        // The backup would end at 520028160, one sector past a target that is one sector smaller.
        long smallerTarget = PartitionBytes - BytesPerSector;

        Assert.Throws<InvalidOperationException>(() => NtfsTailPlan.Plan(
            TotalClusters, BytesPerCluster, NumberSectors, BytesPerSector, PartitionBytes, smallerTarget));
    }
}
