using System.Text;
using Shouldly;
using WinCopy.Domain.BootRepair;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Domain.Tests.Execution;
using Xunit;
using static WinCopy.Domain.Tests.Execution.ConfirmedPlanFixtures;
using static WinCopy.Domain.Tests.Planning.PlanningFixtures;

namespace WinCopy.Domain.Tests.BootRepair;

public sealed class BootRepairSpecTests
{
    // Índices de las particiones del disco del sistema típico, en orden de origen.
    private const int EfiIndex = 0;
    private const int WindowsIndex = 2;
    private const int RecoveryIndex = 3;

    // ----- Valor de MountedDevices en formato GPT -----

    [Fact]
    public void GptValueIsTheDmioPrefixFollowedByTheGuidBytes()
    {
        var guid = new Guid("0f1e2d3c-4b5a-6978-8796-a5b4c3d2e1f0");

        var value = MountedDevicesRewrite.GptValue(guid);

        value.Length.ShouldBe(24);
        Encoding.ASCII.GetString(value, 0, 8).ShouldBe("DMIO:ID:");
        value.AsSpan(8).ToArray().ShouldBe(guid.ToByteArray());
    }

    [Fact]
    public void DifferentGuidsGiveDifferentValues()
    {
        MountedDevicesRewrite.GptValue(new Guid("00000000-0000-0000-0000-000000000001"))
            .ShouldNotBe(MountedDevicesRewrite.GptValue(new Guid("00000000-0000-0000-0000-000000000002")));
    }

    [Fact]
    public void IsStaleRecognisesTheSourceValuesOnly()
    {
        var stale = MountedDevicesRewrite.GptValue(new Guid("00000000-0000-0000-0000-00000000aaaa"));
        var fresh = MountedDevicesRewrite.GptValue(new Guid("00000000-0000-0000-0000-00000000bbbb"));
        var rewrite = new MountedDevicesRewrite('C', fresh, [stale]);

        rewrite.IsStale(stale).ShouldBeTrue();
        rewrite.IsStale(fresh).ShouldBeFalse();
    }

    [Fact]
    public void IsStaleIgnoresValuesThatOnlyShareThePrefix()
    {
        var stale = MountedDevicesRewrite.GptValue(new Guid("00000000-0000-0000-0000-00000000aaaa"));
        var rewrite = new MountedDevicesRewrite('C', MountedDevicesRewrite.GptValue(Guid.NewGuid()), [stale]);
        var otherGuid = MountedDevicesRewrite.GptValue(new Guid("00000000-0000-0000-0000-00000000cccc"));

        rewrite.IsStale(otherGuid).ShouldBeFalse();
        rewrite.IsStale(stale.AsSpan(0, 20)).ShouldBeFalse();
    }

    // ----- Especificación derivada del plan -----

    [Fact]
    public void FromTargetsDriveCWithTheNewWindowsPartitionGuid()
    {
        var plan = Confirmed();

        var spec = BootRepairSpec.From(plan);

        spec.WindowsPartition.ShouldBe(plan.Mappings[WindowsIndex].NewUniqueId);
        spec.MountedDevices.DriveLetter.ShouldBe('C');
        spec.MountedDevices.NewValue.ShouldBe(MountedDevicesRewrite.GptValue(plan.Mappings[WindowsIndex].NewUniqueId.Value));
    }

    [Fact]
    public void FromUsesTheNewEspAndRecoveryGuids()
    {
        var plan = Confirmed();

        var spec = BootRepairSpec.From(plan);

        spec.EspPartition.ShouldBe(plan.Mappings[EfiIndex].NewUniqueId);
        spec.RecoveryPartition.ShouldBe(plan.Mappings[RecoveryIndex].NewUniqueId);
    }

    [Fact]
    public void ValuesToRemoveCoverEverySourcePartition()
    {
        var plan = Confirmed();

        var spec = BootRepairSpec.From(plan);

        spec.MountedDevices.ValuesToRemove.Count.ShouldBe(plan.Mappings.Count);
        for (var i = 0; i < plan.Mappings.Count; i++)
        {
            spec.MountedDevices.ValuesToRemove[i].ShouldBe(MountedDevicesRewrite.GptValue(plan.Mappings[i].Source.UniqueId.Value));
        }
    }

    [Fact]
    public void OldWindowsValueIsStaleAndTheNewOneIsNot()
    {
        var plan = Confirmed();
        var spec = BootRepairSpec.From(plan);
        var oldWindows = MountedDevicesRewrite.GptValue(plan.Mappings[WindowsIndex].Source.UniqueId.Value);

        spec.MountedDevices.IsStale(oldWindows).ShouldBeTrue();
        spec.MountedDevices.IsStale(spec.MountedDevices.NewValue).ShouldBeFalse();
    }

    [Fact]
    public void StepsAreInOrderWithReconfigureWinReWhenThereIsARecoveryPartition()
    {
        var spec = BootRepairSpec.From(Confirmed());

        spec.Steps.ShouldBe(new[]
        {
            BootRepairStep.RewriteMountedDevices,
            BootRepairStep.RebuildBcd,
            BootRepairStep.ReconfigureWinRe,
        });
    }

    [Fact]
    public void WithoutARecoveryPartitionTheWinReStepIsOmitted()
    {
        var system = SystemDisk();
        var withoutRecovery = WithPartitions(system, system.Partitions.Where(partition => partition.Role != PartitionRole.Recovery));

        var spec = BootRepairSpec.From(Confirmed(systemDisk: withoutRecovery));

        spec.RecoveryPartition.ShouldBeNull();
        spec.Steps.ShouldBe(new[] { BootRepairStep.RewriteMountedDevices, BootRepairStep.RebuildBcd });
    }

    [Fact]
    public void FromRejectsAPlanWithoutAnEfiPartition()
    {
        var system = SystemDisk();
        var withoutEfi = WithPartitions(system, system.Partitions.Where(partition => partition.Role != PartitionRole.EfiSystem));
        var plan = Confirmed(systemDisk: withoutEfi);

        Should.Throw<InvalidOperationException>(() => BootRepairSpec.From(plan));
    }

    [Fact]
    public void FromRejectsANullPlan()
    {
        Should.Throw<ArgumentNullException>(() => BootRepairSpec.From(null!));
    }
}
