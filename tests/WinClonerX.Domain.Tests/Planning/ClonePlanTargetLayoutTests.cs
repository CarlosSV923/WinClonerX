using Shouldly;
using WinClonerX.Domain.Planning;
using Xunit;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Planning;

public sealed class ClonePlanTargetLayoutTests
{
    [Fact]
    public void LayoutCarriesTheNewDiskGuidAndTheTargetGeometry()
    {
        var target = TargetDisk();
        var plan = PlanFor(InventoryOf(SystemDisk(), target)).Value;

        var layout = plan.ToTargetLayout(target);

        layout.DiskGuid.ShouldBe(plan.NewDiskGuid);
        layout.DiskSize.ShouldBe(target.Size);
        layout.LogicalSectorSize.ShouldBe(target.LogicalSectorSize);
    }

    [Fact]
    public void EntriesFollowTheMappingsInOrderWithTheNewIdentifiersAndExtents()
    {
        var target = TargetDisk();
        var plan = PlanFor(InventoryOf(SystemDisk(), target)).Value;

        var entries = plan.ToTargetLayout(target).Entries;

        entries.Count.ShouldBe(plan.Mappings.Count);
        for (var i = 0; i < entries.Count; i++)
        {
            entries[i].Type.ShouldBe(plan.Mappings[i].Source.Type);
            entries[i].UniqueId.ShouldBe(plan.Mappings[i].NewUniqueId);
            entries[i].Extent.ShouldBe(plan.Mappings[i].TargetExtent);
        }
    }

    [Fact]
    public void EntriesKeepTheSourceAttributesAndNames()
    {
        var system = SystemDisk();
        var source = WithPartitions(system, system.Partitions.Select(partition => partition.Number == 1
            ? partition with { Attributes = 0x8000000000000001UL, Name = "EFI personalizada" }
            : partition));
        var target = TargetDisk();
        var plan = PlanFor(InventoryOf(source, target)).Value;

        var efi = plan.ToTargetLayout(target).Entries[0];

        efi.Attributes.ShouldBe(0x8000000000000001UL);
        efi.Name.ShouldBe("EFI personalizada");
    }

    [Fact]
    public void LayoutRejectsADiskThatIsNotThePlanTarget()
    {
        var system = SystemDisk();
        var plan = PlanFor(InventoryOf(system, TargetDisk())).Value;

        Should.Throw<InvalidOperationException>(() => plan.ToTargetLayout(system));
    }

    [Fact]
    public void LayoutRejectsANullDisk()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk())).Value;

        Should.Throw<ArgumentNullException>(() => plan.ToTargetLayout(null!));
    }
}
