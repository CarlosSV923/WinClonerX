using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Planning;

/// <summary>Un volumen con BitLocker suspendido también se clona sin cifrar, así que debe avisar igual que uno activo.</summary>
public sealed class PreflightBitLockerSuspendedTests
{
    [Fact]
    public void SuspendedBitLockerOnTheWindowsVolumeIsAWarning()
    {
        var inventory = InventoryOf(SystemDisk(windowsEncryption: EncryptionState.BitLockerSuspended), TargetDisk());

        BitLockerCheck(inventory, Facts()).Severity.ShouldBe(Severity.Warning);
    }

    [Fact]
    public void SuspendedBitLockerReportedByThePlatformFactsIsAWarning()
    {
        var facts = Facts() with
        {
            Encryption = new Dictionary<PartitionGuid, EncryptionState> { [SourcePartitionGuid(3)] = EncryptionState.BitLockerSuspended },
        };

        BitLockerCheck(InventoryOf(SystemDisk(), TargetDisk()), facts).Severity.ShouldBe(Severity.Warning);
    }

    [Fact]
    public void SuspendedBitLockerCountsEveryAffectedVolume()
    {
        var inventory = InventoryOf(SystemDisk(windowsEncryption: EncryptionState.BitLockerSuspended, dataLength: Gib), TargetDisk());
        var facts = Facts() with
        {
            Encryption = new Dictionary<PartitionGuid, EncryptionState> { [SourcePartitionGuid(4)] = EncryptionState.BitLockerSuspended },
        };

        BitLockerCheck(inventory, facts).Facts["Volumes"].ShouldBe(2);
    }

    [Fact]
    public void SuspendedBitLockerAppearsAmongThePlanWarnings()
    {
        var inventory = InventoryOf(SystemDisk(windowsEncryption: EncryptionState.BitLockerSuspended), TargetDisk());

        var plan = PlanFor(inventory).Value;

        plan.Warnings.Select(warning => warning.Code).ShouldContain(PreflightCode.BitLockerOn);
    }

    [Fact]
    public void UnencryptedVolumesPassTheBitLockerCheck()
    {
        BitLockerCheck(InventoryOf(SystemDisk(), TargetDisk()), Facts()).Severity.ShouldBe(Severity.Pass);
    }

    private static PreflightCheck BitLockerCheck(DiskInventory inventory, PlatformFacts facts) =>
        PreflightEvaluator.Evaluate(inventory, facts, CapacityPolicy.Default).Global.Checks
            .Single(check => check.Code == PreflightCode.BitLockerOn);
}
