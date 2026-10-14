using Shouldly;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using static WinCopy.Domain.Tests.Planning.PlanningFixtures;

namespace WinCopy.Domain.Tests.Planning;

public sealed class ClonePlannerTests
{
    private const int WindowsIndex = 2;

    [Fact]
    public void TypicalSetupProducesADraftPlan()
    {
        var inventory = InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib));

        var result = PlanFor(inventory);

        result.IsSuccess.ShouldBeTrue();
        var plan = result.Value;
        plan.Status.ShouldBe(PlanStatus.Draft);
        plan.Source.ShouldBe(SourceId);
        plan.Target.ShouldBe(TargetId);
        plan.Fingerprint.ShouldBe(inventory.Fingerprint(SourceId, TargetId));
        plan.Verification.ShouldBe(VerificationMode.Structural);
        plan.Warnings.ShouldBeEmpty();
    }

    [Fact]
    public void MappingsFollowTheSourcePartitionsInOrder()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib))).Value;

        plan.Mappings.Select(m => m.Source.Number).ShouldBe([1, 2, 3, 4]);
    }

    [Fact]
    public void OnlyTheWindowsPartitionGrowsAndItsFileSystemIsResized()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib))).Value;

        plan.Mappings.Select(m => m.Resize).ShouldBe([ResizeKind.None, ResizeKind.None, ResizeKind.GrowFileSystem, ResizeKind.None]);
        plan.Mappings[WindowsIndex].TargetExtent.Length.ShouldBeGreaterThan(plan.Mappings[WindowsIndex].Source.Extent.Length);
    }

    [Fact]
    public void SameSizeTargetDoesNotResizeAnything()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: 256 * Gib))).Value;

        plan.Mappings.Select(m => m.Resize).ShouldAllBe(r => r == ResizeKind.None);
    }

    [Fact]
    public void CopyMethodIsUsedClustersOnNtfsAndRawForEveryOtherPartition()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(dataLength: 50 * Gib), TargetDisk(size: 512 * Gib))).Value;

        plan.Mappings.Select(m => m.CopyMethod).ShouldBe(
        [
            CopyMethod.RawFull,
            CopyMethod.RawFull,
            CopyMethod.UsedClustersFromSnapshot,
            CopyMethod.UsedClustersFromSnapshot,
            CopyMethod.UsedClustersFromSnapshot,
        ]);
    }

    [Fact]
    public void NewGuidsAreGeneratedInOrderFromTheInjectedFactory()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib))).Value;

        plan.Id.Value.ShouldBe(G(5000), "the plan id is the first generated guid");
        plan.NewDiskGuid.ShouldBe(G(5001), "the disk guid is the second");
        plan.Mappings.Select(m => m.NewUniqueId.Value).ShouldBe([G(5002), G(5003), G(5004), G(5005)]);
    }

    [Fact]
    public void NewGuidsDifferFromTheSourceGuids()
    {
        var source = SystemDisk();
        var plan = PlanFor(InventoryOf(source, TargetDisk(size: 512 * Gib))).Value;

        plan.NewDiskGuid.ShouldNotBe(source.Id.DiskGuid);
        plan.Mappings.Select(m => m.NewUniqueId).ShouldNotContain(p => source.Partitions.Any(s => s.UniqueId == p));
    }

    [Fact]
    public void EstimateCopiesTheUsedClustersOfNtfsAndTheWholeEspAndMsr()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib))).Value;

        // ESP completa (100 MiB) + MSR completa (16 MiB) + Windows usada (40 GiB) + Recovery usada (450 MiB).
        plan.Estimate.BytesToCopy.ShouldBe(new ByteSize(100 * Mib + 16 * Mib + 40 * Gib + 450 * Mib));
    }

    [Fact]
    public void WarningsAreTheGlobalAndTheTargetWarnings()
    {
        var facts = Facts() with { OnBattery = true, BatteryPercent = 80 };

        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk(withDataPartition: true)), facts: facts).Value;

        plan.Warnings.Select(w => w.Code).ShouldBe([PreflightCode.OnBattery, PreflightCode.TargetNotEmpty]);
        plan.Warnings.ShouldAllBe(w => w.Severity == Severity.Warning);
    }

    [Fact]
    public void TheVerificationModeIsKept()
    {
        var plan = PlanFor(InventoryOf(SystemDisk(), TargetDisk()), mode: VerificationMode.StructuralAndFull).Value;

        plan.Verification.ShouldBe(VerificationMode.StructuralAndFull);
    }

    [Fact]
    public void TheSystemDiskAsTargetIsRejectedWithTargetIsSystem()
    {
        var result = PlanFor(InventoryOf(SystemDisk(), TargetDisk()), target: SourceId);

        result.IsSuccess.ShouldBeFalse();
        var check = result.Error.Checks.Single(c => c.Code == PreflightCode.TargetIsSystem);
        check.Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void ATargetOutsideTheInventoryIsAnArgumentError()
    {
        Should.Throw<ArgumentException>(() => PlanFor(InventoryOf(SystemDisk(), TargetDisk()), target: OtherTargetId));
    }

    [Fact]
    public void AGlobalBlockingCheckRejectsThePlanWithTheFullReport()
    {
        var facts = Facts() with { SnapshotAvailable = false };

        var result = PlanFor(InventoryOf(SystemDisk(), TargetDisk()), facts: facts);

        result.IsSuccess.ShouldBeFalse();
        result.Error.IsBlocked.ShouldBeTrue();
        result.Error.Checks.Select(c => c.Code).ShouldContain(PreflightCode.SnapshotUnavailable);
    }

    [Fact]
    public void ATargetThatIsTooSmallRejectsThePlanWithTargetTooSmall()
    {
        var result = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: 200 * Gib)));

        result.IsSuccess.ShouldBeFalse();
        var check = result.Error.Checks.Single(c => c.Code == PreflightCode.TargetTooSmall);
        check.Facts["Shortfall"].ShouldBe(ByteSize.FromGiB(56));
    }

    [Fact]
    public void ATargetInUseRejectsThePlan()
    {
        var facts = Facts() with { TargetInUse = new Dictionary<DiskId, bool> { [TargetId] = true } };

        var result = PlanFor(InventoryOf(SystemDisk(), TargetDisk()), facts: facts);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Checks.Select(c => c.Code).ShouldContain(PreflightCode.TargetInUse);
    }

    [Fact]
    public void AFirmwareThatIsNotUefiRejectsThePlan()
    {
        var inventory = new DiskInventory(CapturedAt, FirmwareType.Bios, [SystemDisk(), TargetDisk()]);

        var result = PlanFor(inventory);

        result.IsSuccess.ShouldBeFalse();
        result.Error.Checks.Select(c => c.Code).ShouldContain(PreflightCode.FirmwareNotUefi);
    }

    [Fact]
    public void AnUnsupportedInventoryRejectsThePlan()
    {
        var result = PlanFor(InventoryOf(TargetDisk()));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Checks.Select(c => c.Code).ShouldContain(PreflightCode.UnsupportedDiskKind);
    }

    [Fact]
    public void TheSameInputsGiveTheSameFingerprintAndGuids()
    {
        var inventory = InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib));

        var first = PlanFor(inventory).Value;
        var second = PlanFor(inventory).Value;

        second.Fingerprint.ShouldBe(first.Fingerprint);
        second.NewDiskGuid.ShouldBe(first.NewDiskGuid);
    }
}
