using Shouldly;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using static WinCopy.Domain.Tests.Planning.PlanningFixtures;

namespace WinCopy.Domain.Tests.Planning;

public sealed class CloneEstimateTests
{
    [Fact]
    public void UsedClustersCopyOnlyTheUsedSpaceAndRawCopiesTheWholePartition()
    {
        var mappings = new[]
        {
            Mapping(Partition(1, 100 * Mib, GptPartitionType.EfiSystem, null), CopyMethod.RawFull),
            Mapping(Partition(3, 200 * Gib, GptPartitionType.BasicData, Ntfs(used: 40 * Gib)), CopyMethod.UsedClustersFromSnapshot),
        };

        var estimate = CloneEstimate.Calculate(mappings, BusKind.Nvme, BusKind.Nvme);

        estimate.BytesToCopy.ShouldBe(new ByteSize(100 * Mib + 40 * Gib));
    }

    [Theory]
    [InlineData(BusKind.Nvme, BusKind.Nvme, 1000)]
    [InlineData(BusKind.Sata, BusKind.Nvme, 350)]
    [InlineData(BusKind.Nvme, BusKind.Sas, 350)]
    [InlineData(BusKind.Nvme, BusKind.Usb, 120)]
    [InlineData(BusKind.FileBackedVirtual, BusKind.Nvme, 200)]
    [InlineData(BusKind.Spaces, BusKind.Nvme, 100)]
    [InlineData(BusKind.Other, BusKind.Nvme, 100)]
    public void DurationUsesTheSlowerBusThroughput(BusKind sourceBus, BusKind targetBus, int mbPerSecond)
    {
        // Un volumen de exactamente mbPerSecond MB (decimales) tarda exactamente un segundo.
        var mappings = new[] { Mapping(Partition(1, mbPerSecond * 1_000_000L, GptPartitionType.MicrosoftReserved, null), CopyMethod.RawFull) };

        var estimate = CloneEstimate.Calculate(mappings, sourceBus, targetBus);

        estimate.EstimatedDuration.ShouldBe(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void EmptyPlanCopiesNothingAndTakesNoTime()
    {
        var estimate = CloneEstimate.Calculate(Array.Empty<PartitionMapping>(), BusKind.Nvme, BusKind.Nvme);

        estimate.BytesToCopy.ShouldBe(new ByteSize(0));
        estimate.EstimatedDuration.ShouldBe(TimeSpan.Zero);
    }

    private static Partition Partition(int number, long length, GptPartitionType type, Volume? volume) =>
        new(number, new Extent(Mib, length), type, new PartitionGuid(G(1000 + number)), 0UL, "Partición", volume);

    private static Volume Ntfs(long used) =>
        new(FileSystemKind.Ntfs, new ByteSize(used * 2), new ByteSize(used), new ByteSize(4096), EncryptionState.None, false, null);

    private static PartitionMapping Mapping(Partition source, CopyMethod method) =>
        new(source, source.Extent, new PartitionGuid(G(5000 + source.Number)), method, ResizeKind.None);
}
