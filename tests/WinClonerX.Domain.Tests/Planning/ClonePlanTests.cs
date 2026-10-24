using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Planning;

public sealed class ClonePlanTests
{
    private const int EfiIndex = 0;
    private const int MsrIndex = 1;
    private const int WindowsIndex = 2;
    private const int RecoveryIndex = 3;

    private static readonly DateTimeOffset At = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    // ----- Invariante 1: destino distinto del origen y que no sea el disco del sistema -----

    [Fact]
    public void Invariant1RejectsATargetEqualToTheSource()
    {
        var source = SystemDisk();

        AssertInvariant(1, () => Build(source, source));
    }

    [Fact]
    public void Invariant1RejectsASystemDiskAsTarget()
    {
        var source = SystemDisk();
        var target = TargetDisk() with { IsSystem = true };

        AssertInvariant(1, () => Build(source, target));
    }

    // ----- Invariante 2: mismo tamaño de sector lógico -----

    [Fact]
    public void Invariant2RejectsDifferentLogicalSectorSizes()
    {
        var source = SystemDisk();
        var target = TargetDisk(size: 512 * Gib, sector: 4096);

        AssertInvariant(2, () => Build(source, target));
    }

    // ----- Invariante 3: extensiones sin solape, alineadas a 1 MiB, en orden y dentro del área utilizable -----

    [Fact]
    public void Invariant3RejectsOverlappingExtents()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[RecoveryIndex] = mappings[RecoveryIndex] with
        {
            TargetExtent = new Extent(mappings[WindowsIndex].TargetExtent.Offset + Mib, RecoveryLength),
        };

        AssertInvariant(3, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant3RejectsAnOffsetNotAlignedToOneMebibyte()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[EfiIndex] = mappings[EfiIndex] with { TargetExtent = new Extent(Mib + 512, 99 * Mib) };

        AssertInvariant(3, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant3RejectsExtentsOutOfSourceOrder()
    {
        var (source, target, mappings) = ValidSetup();
        (mappings[EfiIndex], mappings[MsrIndex]) = (mappings[MsrIndex], mappings[EfiIndex]);

        AssertInvariant(3, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant3RejectsAnExtentPastTheUsableArea()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[RecoveryIndex] = mappings[RecoveryIndex] with { TargetExtent = new Extent(target.Size.Bytes - Mib, RecoveryLength) };

        AssertInvariant(3, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant3RejectsTheWrongNumberOfMappings()
    {
        var (source, target, mappings) = ValidSetup();

        AssertInvariant(3, () => Build(source, target, mappings.Take(3).ToArray()));
    }

    // ----- Invariante 4: solo Windows puede cambiar de longitud (en la versión 1, solo crecer) -----

    [Fact]
    public void Invariant4RejectsAChangedLengthOnTheEfiPartition()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[EfiIndex] = mappings[EfiIndex] with { TargetExtent = new Extent(Mib, 90 * Mib) };

        AssertInvariant(4, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant4RejectsAChangedLengthOnTheRecoveryPartition()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[RecoveryIndex] = mappings[RecoveryIndex] with
        {
            TargetExtent = new Extent(mappings[RecoveryIndex].TargetExtent.Offset, RecoveryLength - Mib),
        };

        AssertInvariant(4, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant4RejectsAWindowsPartitionThatShrinksInVersionOne()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[WindowsIndex] = mappings[WindowsIndex] with
        {
            TargetExtent = new Extent(mappings[WindowsIndex].TargetExtent.Offset, mappings[WindowsIndex].TargetExtent.Length - Mib),
        };

        AssertInvariant(4, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant4AllowsTheWindowsPartitionToGrow()
    {
        var (source, target, mappings) = ValidSetup(512 * Gib);

        Should.NotThrow(() => { Build(source, target, mappings); });
    }

    // ----- Invariante 6: lo ocupado más el margen cabe en la capacidad asignada a Windows -----

    [Fact]
    public void Invariant6RejectsWindowsWithoutRoomForUsedPlusMargin()
    {
        var source = SystemDisk(windowsUsed: 250 * Gib);
        var target = TargetDisk(size: 256 * Gib);

        AssertInvariant(6, () => Build(source, target, ValidMappings(source, target)));
    }

    [Fact]
    public void Invariant6AcceptsAnExactFitAndRejectsOneByteLess()
    {
        var source = SystemDisk();
        var target = TargetDisk(size: 256 * Gib);
        var windowsLength = ValidMappings(source, target)[WindowsIndex].TargetExtent.Length;
        const long used = 40 * Gib;
        var exactMargin = new CapacityPolicy(0m, new ByteSize(windowsLength - used));

        Should.NotThrow(() => { Build(source, target, ValidMappings(source, target), policy: exactMargin); });

        var oneByteMore = new CapacityPolicy(0m, new ByteSize(windowsLength - used + 1));
        AssertInvariant(6, () => Build(source, target, ValidMappings(source, target), policy: oneByteMore));
    }

    // ----- Invariante 7: GUID nuevos únicos y distintos de los del origen -----

    [Fact]
    public void Invariant7RejectsANewDiskGuidEqualToTheSourceDiskGuid()
    {
        var (source, target, mappings) = ValidSetup();

        AssertInvariant(7, () => Build(source, target, mappings, diskGuid: source.Id.DiskGuid));
    }

    [Fact]
    public void Invariant7RejectsAnEmptyNewDiskGuid()
    {
        var (source, target, mappings) = ValidSetup();

        AssertInvariant(7, () => Build(source, target, mappings, diskGuid: Guid.Empty));
    }

    [Fact]
    public void Invariant7RejectsANewPartitionGuidEqualToASourcePartitionGuid()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[WindowsIndex] = mappings[WindowsIndex] with { NewUniqueId = SourcePartitionGuid(1) };

        AssertInvariant(7, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant7RejectsDuplicatedNewPartitionGuids()
    {
        var (source, target, mappings) = ValidSetup();
        mappings[RecoveryIndex] = mappings[RecoveryIndex] with { NewUniqueId = mappings[EfiIndex].NewUniqueId };

        AssertInvariant(7, () => Build(source, target, mappings));
    }

    [Fact]
    public void Invariant7RejectsANewDiskGuidEqualToANewPartitionGuid()
    {
        var (source, target, mappings) = ValidSetup();

        AssertInvariant(7, () => Build(source, target, mappings, diskGuid: mappings[EfiIndex].NewUniqueId.Value));
    }

    [Fact]
    public void AValidPlanIsCreatedWithoutViolations()
    {
        var (source, target, mappings) = ValidSetup();

        var plan = Build(source, target, mappings);

        plan.Status.ShouldBe(PlanStatus.Draft);
        plan.Mappings.Count.ShouldBe(4);
        plan.NewDiskGuid.ShouldBe(G(9001));
    }

    // ----- Transiciones: Draft -> Confirmed -----

    [Fact]
    public void ANewPlanIsDraftWithNoAcceptedWarnings()
    {
        var plan = BuildWithWarnings();

        plan.Status.ShouldBe(PlanStatus.Draft);
        plan.AcceptedWarnings.ShouldBeEmpty();
        plan.Confirmation.ShouldBeNull();
        plan.InvalidationReason.ShouldBeNull();
    }

    [Fact]
    public void ConfirmWithoutAcceptingTheWarningsIsRejected()
    {
        var plan = BuildWithWarnings();
        var target = TargetDisk();

        Should.Throw<InvalidOperationException>(() => plan.Confirm(Confirmation(target), target));

        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    [Fact]
    public void ConfirmWithOnlySomeWarningsAcceptedIsRejected()
    {
        var plan = BuildWithWarnings(Check(PreflightCode.OnBattery, Severity.Warning), Check(PreflightCode.BitLockerOn, Severity.Warning));
        var target = TargetDisk();
        plan.AcceptWarnings([PreflightCode.OnBattery]);

        Should.Throw<InvalidOperationException>(() => plan.Confirm(Confirmation(target), target));
    }

    [Fact]
    public void ConfirmWithAcceptedWarningsAndTheRightConfirmationMovesToConfirmed()
    {
        var plan = BuildWithWarnings(Check(PreflightCode.OnBattery, Severity.Warning));
        var target = TargetDisk(serial: "SN-ABCD1234");
        plan.AcceptWarnings([PreflightCode.OnBattery]);
        var confirmation = new TargetConfirmation(target.Id, "1234", At);

        plan.Confirm(confirmation, target);

        plan.Status.ShouldBe(PlanStatus.Confirmed);
        plan.Confirmation.ShouldBe(confirmation);
        plan.AcceptedWarnings.ShouldContain(PreflightCode.OnBattery);
    }

    [Fact]
    public void ConfirmWithoutWarningsAndWithTheRightConfirmationMovesToConfirmed()
    {
        var plan = Build(SystemDisk(), TargetDisk(serial: "SN-ABCD1234"));
        var target = TargetDisk(serial: "SN-ABCD1234");

        plan.Confirm(new TargetConfirmation(target.Id, "1234", At), target);

        plan.Status.ShouldBe(PlanStatus.Confirmed);
    }

    [Fact]
    public void ConfirmWithAWrongSuffixIsRejected()
    {
        var plan = Build(SystemDisk(), TargetDisk(serial: "SN-ABCD1234"));
        var target = TargetDisk(serial: "SN-ABCD1234");

        Should.Throw<InvalidOperationException>(() => plan.Confirm(new TargetConfirmation(target.Id, "0000", At), target));

        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    [Fact]
    public void ConfirmWithAConfirmationForAnotherDiskIsRejected()
    {
        var plan = Build(SystemDisk(), TargetDisk(serial: "SN-ABCD1234"));
        var other = TargetDisk(serial: "SN-ABCD1234", id: OtherTargetId);

        Should.Throw<InvalidOperationException>(() => plan.Confirm(new TargetConfirmation(other.Id, "1234", At), other));
    }

    [Fact]
    public void ConfirmWithTheRightConfirmationButAnotherTargetDiskIsRejected()
    {
        var plan = Build(SystemDisk(), TargetDisk(serial: "SN-ABCD1234"));
        var other = TargetDisk(serial: "SN-ABCD1234", id: OtherTargetId);

        Should.Throw<InvalidOperationException>(() => plan.Confirm(new TargetConfirmation(TargetId, "1234", At), other));
        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    [Fact]
    public void ConfirmingTwiceIsRejected()
    {
        var plan = Build(SystemDisk(), TargetDisk(serial: "SN-ABCD1234"));
        var target = TargetDisk(serial: "SN-ABCD1234");
        plan.Confirm(new TargetConfirmation(target.Id, "1234", At), target);

        Should.Throw<InvalidOperationException>(() => plan.Confirm(new TargetConfirmation(target.Id, "1234", At), target));
    }

    [Fact]
    public void AcceptingWarningsAfterConfirmationIsRejected()
    {
        var plan = BuildWithWarnings(Check(PreflightCode.OnBattery, Severity.Warning));
        var target = TargetDisk(serial: "SN-ABCD1234");
        plan.AcceptWarnings([PreflightCode.OnBattery]);
        plan.Confirm(new TargetConfirmation(target.Id, "1234", At), target);

        Should.Throw<InvalidOperationException>(() => plan.AcceptWarnings([PreflightCode.OnBattery]));
    }

    [Fact]
    public void AcceptingAWarningThatThePlanDoesNotHaveIsRejected()
    {
        var plan = BuildWithWarnings(Check(PreflightCode.OnBattery, Severity.Warning));

        Should.Throw<ArgumentException>(() => plan.AcceptWarnings([PreflightCode.BitLockerOn]));
        plan.AcceptedWarnings.ShouldBeEmpty();
    }

    // ----- Invalidate -----

    [Fact]
    public void InvalidateMovesADraftToInvalidatedWithTheReason()
    {
        var plan = Build(SystemDisk(), TargetDisk());

        plan.Invalidate("Cambió la lista de discos");

        plan.Status.ShouldBe(PlanStatus.Invalidated);
        plan.InvalidationReason.ShouldBe("Cambió la lista de discos");
    }

    [Fact]
    public void InvalidateAlsoWorksOnAConfirmedPlan()
    {
        var plan = Build(SystemDisk(), TargetDisk(serial: "SN-ABCD1234"));
        var target = TargetDisk(serial: "SN-ABCD1234");
        plan.Confirm(new TargetConfirmation(target.Id, "1234", At), target);

        plan.Invalidate("La huella cambió");

        plan.Status.ShouldBe(PlanStatus.Invalidated);
    }

    [Fact]
    public void AnInvalidatedPlanCannotBeConfirmed()
    {
        var plan = Build(SystemDisk(), TargetDisk(serial: "SN-ABCD1234"));
        var target = TargetDisk(serial: "SN-ABCD1234");
        plan.Invalidate("cambio");

        Should.Throw<InvalidOperationException>(() => plan.Confirm(new TargetConfirmation(target.Id, "1234", At), target));
    }

    [Fact]
    public void InvalidatingTwiceIsRejected()
    {
        var plan = Build(SystemDisk(), TargetDisk());
        plan.Invalidate("cambio");

        Should.Throw<InvalidOperationException>(() => plan.Invalidate("otro cambio"));
    }

    [Fact]
    public void InvalidateRequiresAReason()
    {
        var plan = Build(SystemDisk(), TargetDisk());

        Should.Throw<ArgumentException>(() => plan.Invalidate("   "));
        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    // ----- Helpers -----

    private static (Disk Source, Disk Target, PartitionMapping[] Mappings) ValidSetup(long targetSize = 256 * Gib)
    {
        var source = SystemDisk();
        var target = TargetDisk(size: targetSize);
        return (source, target, ValidMappings(source, target));
    }

    private static PartitionMapping[] ValidMappings(Disk source, Disk target)
    {
        var extents = TargetLayoutCalculator.Calculate(source, target).Value;
        return source.Partitions
            .Select((partition, i) => new PartitionMapping(
                partition,
                extents[i],
                new PartitionGuid(G(5000 + i)),
                partition.Volume?.FileSystem == FileSystemKind.Ntfs ? CopyMethod.UsedClustersFromSnapshot : CopyMethod.RawFull,
                partition.Role == PartitionRole.Windows && extents[i].Length > partition.Extent.Length
                    ? ResizeKind.GrowFileSystem
                    : ResizeKind.None))
            .ToArray();
    }

    private static ClonePlan Build(
        Disk source,
        Disk target,
        IReadOnlyList<PartitionMapping>? mappings = null,
        Guid? diskGuid = null,
        CapacityPolicy? policy = null,
        IReadOnlyList<PreflightCheck>? warnings = null) =>
        ClonePlan.Create(
            new PlanId(G(9000)),
            source,
            target,
            InventoryFingerprint.Compute(["huella de prueba"]),
            mappings ?? ValidMappings(source, target),
            diskGuid ?? G(9001),
            VerificationMode.Structural,
            warnings ?? Array.Empty<PreflightCheck>(),
            new CloneEstimate(new ByteSize(1), TimeSpan.Zero, TimeSpan.Zero),
            policy ?? CapacityPolicy.Default);

    private static ClonePlan BuildWithWarnings(params PreflightCheck[] warnings)
    {
        var source = SystemDisk();
        var target = TargetDisk(serial: "SN-ABCD1234");
        var effective = warnings.Length == 0 ? new[] { Check(PreflightCode.OnBattery, Severity.Warning) } : warnings;
        return Build(source, target, warnings: effective);
    }

    private static TargetConfirmation Confirmation(Disk target) =>
        new(target.Id, TargetConfirmation.ExpectedSuffix(target), At);

    private static void AssertInvariant(int number, Action action)
    {
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain($"Invariante {number}");
    }
}
