using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Planning;

public sealed class PreflightEvaluatorTests
{
    [Fact]
    public void TypicalSetupHasNoBlockingChecksAndTheTargetIsEligible()
    {
        var (global, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk()), Facts());

        global.IsBlocked.ShouldBeFalse();
        global.Warnings.ShouldBeEmpty();
        var target = targets.Single(t => t.Disk == TargetId);
        target.IsEligible.ShouldBeTrue();
        target.Reasons.ShouldBeEmpty();
        target.Strategy.ShouldBe(SizeStrategy.SameOrLarger);
        target.Shortfall.ShouldBeNull();
    }

    [Fact]
    public void FirmwareNotUefiBlocks()
    {
        var inventory = new DiskInventory(CapturedAt, FirmwareType.Bios, [SystemDisk(), TargetDisk()]);

        Global(inventory, Facts(), PreflightCode.FirmwareNotUefi).Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void FirmwareUefiPasses()
    {
        Global(InventoryOf(SystemDisk(), TargetDisk()), Facts(), PreflightCode.FirmwareNotUefi).Severity.ShouldBe(Severity.Pass);
    }

    [Fact]
    public void InventoryWithoutSystemDiskIsUnsupportedWithTheReason()
    {
        var (global, targets) = Evaluate(InventoryOf(TargetDisk()), Facts());

        var check = global.Checks.Single(c => c.Code == PreflightCode.UnsupportedDiskKind);
        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Reason"].ShouldBe("NoSystemDisk");
        targets.ShouldBeEmpty();
    }

    [Fact]
    public void InventoryWithTwoSystemDisksIsUnsupportedWithTheReason()
    {
        var inventory = InventoryOf(SystemDisk(), SystemDisk() with { Id = TargetId });

        var (global, targets) = Evaluate(inventory, Facts());

        var check = global.Checks.Single(c => c.Code == PreflightCode.UnsupportedDiskKind);
        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Reason"].ShouldBe("MultipleSystemDisks");
        targets.ShouldBeEmpty();
    }

    [Fact]
    public void SourceWithLdmPartitionIsUnsupported()
    {
        var system = SystemDisk();
        var source = WithPartitions(system, system.Partitions.Append(
            new Partition(9, new Extent(900 * Mib, Mib), GptPartitionType.LdmData, new PartitionGuid(G(1900)), 0UL, "LDM", null)));

        var check = Global(InventoryOf(source, TargetDisk()), Facts(), PreflightCode.UnsupportedDiskKind);

        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Reason"].ShouldBe("UnsupportedPartition");
    }

    [Theory]
    [InlineData(BusKind.Spaces)]
    [InlineData(BusKind.Raid)]
    public void SourceOnSpacesOrRaidBusIsUnsupported(BusKind bus)
    {
        var source = SystemDisk() with { Bus = bus };

        var check = Global(InventoryOf(source, TargetDisk()), Facts(), PreflightCode.UnsupportedDiskKind);

        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Reason"].ShouldBe("UnsupportedBus");
    }

    [Fact]
    public void SourceNotGptBlocks()
    {
        var source = SystemDisk() with { PartitionStyle = PartitionStyle.Mbr };

        Global(InventoryOf(source, TargetDisk()), Facts(), PreflightCode.SourceNotGpt).Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void SourceWithoutWindowsPartitionIsMultipleWindowsWithZeroCount()
    {
        var system = SystemDisk();
        var source = WithPartitions(system, system.Partitions.Select(p => p.Role == PartitionRole.Windows
            ? p with { Volume = p.Volume! with { IsWindowsVolume = false } }
            : p));

        var check = Global(InventoryOf(source, TargetDisk()), Facts(), PreflightCode.MultipleWindows);

        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["WindowsPartitions"].ShouldBe(0);
    }

    [Fact]
    public void MoreThanOneWindowsVolumeIsMultipleWindows()
    {
        var system = SystemDisk();
        var source = WithPartitions(system, system.Partitions.Select(p => p.Role == PartitionRole.Recovery
            ? p with { Volume = p.Volume! with { IsWindowsVolume = true } }
            : p));

        var check = Global(InventoryOf(source, TargetDisk()), Facts(), PreflightCode.MultipleWindows);

        check.Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void SingleWindowsPartitionPasses()
    {
        Global(InventoryOf(SystemDisk(), TargetDisk()), Facts(), PreflightCode.MultipleWindows).Severity.ShouldBe(Severity.Pass);
    }

    [Fact]
    public void SnapshotUnavailableBlocks()
    {
        var facts = Facts() with { SnapshotAvailable = false };

        Global(InventoryOf(SystemDisk(), TargetDisk()), facts, PreflightCode.SnapshotUnavailable).Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void SnapshotSpaceBelowTwoGibBlocks()
    {
        var facts = Facts() with { SnapshotFreeSpace = ByteSize.FromGiB(1) };

        Global(InventoryOf(SystemDisk(), TargetDisk()), facts, PreflightCode.SnapshotSpaceLow).Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void SnapshotSpaceOfExactlyTwoGibPasses()
    {
        var facts = Facts() with { SnapshotFreeSpace = ByteSize.FromGiB(2) };

        Global(InventoryOf(SystemDisk(), TargetDisk()), facts, PreflightCode.SnapshotSpaceLow).Severity.ShouldBe(Severity.Pass);
    }

    [Fact]
    public void BitLockerOnTheWindowsVolumeIsAWarning()
    {
        var inventory = InventoryOf(SystemDisk(windowsEncryption: EncryptionState.BitLockerOn), TargetDisk());

        Global(inventory, Facts(), PreflightCode.BitLockerOn).Severity.ShouldBe(Severity.Warning);
    }

    [Fact]
    public void BitLockerReportedByThePlatformFactsIsAWarning()
    {
        var facts = Facts() with
        {
            Encryption = new Dictionary<PartitionGuid, EncryptionState> { [SourcePartitionGuid(3)] = EncryptionState.BitLockerOn },
        };

        Global(InventoryOf(SystemDisk(), TargetDisk()), facts, PreflightCode.BitLockerOn).Severity.ShouldBe(Severity.Warning);
    }

    [Fact]
    public void OnBatteryIsAWarningWithTheBatteryLevel()
    {
        var facts = Facts() with { OnBattery = true, BatteryPercent = 42 };

        var check = Global(InventoryOf(SystemDisk(), TargetDisk()), facts, PreflightCode.OnBattery);

        check.Severity.ShouldBe(Severity.Warning);
        check.Facts["BatteryPercent"].ShouldBe(42);
    }

    [Fact]
    public void OnAcPowerPasses()
    {
        Global(InventoryOf(SystemDisk(), TargetDisk()), Facts(), PreflightCode.OnBattery).Severity.ShouldBe(Severity.Pass);
    }

    [Fact]
    public void SectorSizeMismatchBlocksTheTarget()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(sector: 4096)), Facts());

        Reason(targets, PreflightCode.SectorSizeMismatch).ShouldNotBeNull().Severity.ShouldBe(Severity.Blocking);
        targets.Single().IsEligible.ShouldBeFalse();
    }

    [Fact]
    public void TargetInUseByFactsBlocksTheTarget()
    {
        var facts = Facts() with { TargetInUse = new Dictionary<DiskId, bool> { [TargetId] = true, [OtherTargetId] = false } };

        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk()), facts);

        Reason(targets, PreflightCode.TargetInUse).ShouldNotBeNull().Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void ReadOnlyTargetBlocksAsInUse()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(isReadOnly: true)), Facts());

        Reason(targets, PreflightCode.TargetInUse).ShouldNotBeNull().Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void TargetWithAVolumeInUseByOthersBlocksAsInUse()
    {
        var target = TargetDisk(withDataPartition: true);
        var inUse = WithPartitions(target, target.Partitions.Select(p => p with { Volume = p.Volume! with { InUseByOthers = true } }));

        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), inUse), Facts());

        Reason(targets, PreflightCode.TargetInUse).ShouldNotBeNull().Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void TargetMissingFromTheFactsIsTreatedAsInUse()
    {
        var facts = Facts() with { TargetInUse = new Dictionary<DiskId, bool>() };

        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk()), facts);

        Reason(targets, PreflightCode.TargetInUse).ShouldNotBeNull().Severity.ShouldBe(Severity.Blocking);
    }

    [Fact]
    public void TargetSmallerThanTheWindowsPartitionIsTooSmallWithTheShortfall()
    {
        // Windows ocupa 256 GiB - 868 MiB en el origen; en 200 GiB faltan exactamente 56 GiB.
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(size: 200 * Gib)), Facts());

        var check = Reason(targets, PreflightCode.TargetTooSmall).ShouldNotBeNull();
        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Shortfall"].ShouldBe(ByteSize.FromGiB(56));
        var target = targets.Single();
        target.IsEligible.ShouldBeFalse();
        target.Shortfall.ShouldBe(ByteSize.FromGiB(56));
        target.Strategy.ShouldBeNull();
    }

    [Fact]
    public void TargetThatCannotKeepTheMarginIsTooSmall()
    {
        // 250 GiB ocupados exigen 275 GiB con el margen de 25 GiB; el origen solo tiene ~255,15 GiB.
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(windowsUsed: 250 * Gib), TargetDisk(size: 256 * Gib)), Facts());

        var check = Reason(targets, PreflightCode.TargetTooSmall).ShouldNotBeNull();
        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Shortfall"].ShouldBe(ByteSize.FromGiB(19) + ByteSize.FromMiB(868));
    }

    [Fact]
    public void TargetWithPartitionsIsANotEmptyWarningButStillEligible()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(withDataPartition: true)), Facts());

        Reason(targets, PreflightCode.TargetNotEmpty).ShouldNotBeNull().Severity.ShouldBe(Severity.Warning);
        var target = targets.Single();
        target.IsEligible.ShouldBeTrue();
        target.Reasons.Single().Code.ShouldBe(PreflightCode.TargetNotEmpty);
    }

    [Fact]
    public void EmptyTargetHasNoNotEmptyReason()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk()), Facts());

        Reason(targets, PreflightCode.TargetNotEmpty).ShouldBeNull();
    }

    [Fact]
    public void TheSystemDiskIsNeverATarget()
    {
        var system = SystemDisk();
        var inventory = InventoryOf(system, TargetDisk(), TargetDisk(size: 1024 * Gib, id: OtherTargetId));

        var (_, targets) = Evaluate(inventory, Facts());

        targets.Select(t => t.Disk).ShouldBe([TargetId, OtherTargetId]);
        targets.Any(t => t.Disk == system.Id).ShouldBeFalse();
    }

    [Fact]
    public void ReasonsOnlyListWarningsAndBlockingChecks()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(withDataPartition: true, sector: 4096)), Facts());

        var target = targets.Single();
        target.Reasons.ShouldAllBe(c => c.Severity != Severity.Pass);
        target.Reasons.Select(c => c.Code).ShouldBe([PreflightCode.SectorSizeMismatch, PreflightCode.TargetNotEmpty]);
    }

    [Fact]
    public void ShrinkInsufficientIsNotEmittedInVersionOne()
    {
        var (global, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(size: 200 * Gib)), Facts());

        global.Checks.Any(c => c.Code == PreflightCode.ShrinkInsufficient).ShouldBeFalse();
        targets.SelectMany(t => t.Reasons).Any(c => c.Code == PreflightCode.ShrinkInsufficient).ShouldBeFalse();
    }

    [Fact]
    public void GlobalChecksAreReportedEvenWhenSeveralFail()
    {
        var facts = Facts() with { SnapshotAvailable = false, OnBattery = true };

        var (global, _) = Evaluate(InventoryOf(SystemDisk(), TargetDisk()), facts);

        global.Checks.Select(c => c.Code).ShouldContain(PreflightCode.SnapshotUnavailable);
        global.Checks.Select(c => c.Code).ShouldContain(PreflightCode.OnBattery);
        global.IsBlocked.ShouldBeTrue();
    }

    private static (PreflightReport Global, IReadOnlyList<TargetEligibility> Targets) Evaluate(DiskInventory inventory, PlatformFacts facts) =>
        PreflightEvaluator.Evaluate(inventory, facts, CapacityPolicy.Default);

    private static PreflightCheck Global(DiskInventory inventory, PlatformFacts facts, PreflightCode code) =>
        Evaluate(inventory, facts).Global.Checks.Single(c => c.Code == code);

    private static PreflightCheck? Reason(IReadOnlyList<TargetEligibility> targets, PreflightCode code) =>
        targets.Single().Reasons.SingleOrDefault(c => c.Code == code);
}
