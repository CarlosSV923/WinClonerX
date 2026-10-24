using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;
using static WinClonerX.Domain.Tests.Planning.ShrinkFixtures;

namespace WinClonerX.Domain.Tests.Planning;

public sealed class ClonePlanShrinkTests
{
    private const int WindowsIndex = 2;

    private static readonly DateTimeOffset At = new(2026, 10, 13, 12, 0, 0, TimeSpan.Zero);

    // ----- El planificador construye la reducción -----

    [Fact]
    public void PlannerBuildsASourceShrinkWhenTheSmallerTargetNeedsIt()
    {
        var (plan, _) = ShrinkPlan(ByteSize.FromGiB(45));

        var shrink = plan.SourceShrink.ShouldNotBeNull();
        shrink.Partition.ShouldBe(SourcePartitionGuid(3));
        shrink.OriginalLength.ShouldBe(new ByteSize(SourceWindowsLength));
        shrink.ShrunkLength.ShouldBe(new ByteSize(SmallTargetWindowsLength));
        shrink.MinimumSupported.ShouldBe(ByteSize.FromGiB(45));
    }

    [Fact]
    public void ThePlanKeepsTheSourceExtentAndGivesWindowsTheRemainingTargetSpace()
    {
        var (plan, _) = ShrinkPlan(ByteSize.FromGiB(45));
        var windows = plan.Mappings[WindowsIndex];

        windows.Source.Extent.Length.ShouldBe(SourceWindowsLength, "the shrink happens at execution; the source extent is the current one");
        windows.TargetExtent.Length.ShouldBe(SmallTargetWindowsLength);
        windows.Resize.ShouldBe(ResizeKind.None, "the shrunk source and target Windows lengths match");
    }

    [Fact]
    public void ShrunkWindowsThatFillItsSpaceExactlyDoesNotGrowTheFileSystem()
    {
        var (plan, _) = ShrinkPlan(new ByteSize(SmallTargetWindowsLength));

        plan.SourceShrink.ShouldNotBeNull().ShrunkLength.Bytes.ShouldBe(SmallTargetWindowsLength);
        plan.Mappings[WindowsIndex].Resize.ShouldBe(ResizeKind.None);
    }

    [Fact]
    public void ASameOrLargerPlanHasNoSourceShrink()
    {
        var result = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib)), facts: FactsWithMinimum(ByteSize.FromGiB(45)));

        result.IsSuccess.ShouldBeTrue();
        result.Value.SourceShrink.ShouldBeNull();
    }

    [Fact]
    public void WithoutAKnownMinimumThePlannerRefusesTheSmallerTarget()
    {
        var result = PlanFor(InventoryOf(SystemDisk(), TargetDisk(size: SmallTargetSize)), facts: FactsWithMinimum(null));

        result.IsSuccess.ShouldBeFalse();
        result.Error.Checks.Any(check => check.Code == PreflightCode.TargetTooSmall && check.Severity == Severity.Blocking).ShouldBeTrue();
    }

    // ----- Invariante 5: la reducción es coherente con el destino de Windows -----

    [Fact]
    public void AConsistentShrinkSetupIsAccepted()
    {
        var setup = ShrinkSetup();

        Should.NotThrow(() => { Create(setup.Source, setup.Target, setup.Mappings, setup.Shrink); });
    }

    [Fact]
    public void Invariant5RejectsAShrunkLengthLargerThanTheWindowsTargetLength()
    {
        var setup = ShrinkSetup();
        var bigger = new SourceShrinkStep(
            setup.Shrink.Partition,
            setup.Shrink.OriginalLength,
            new ByteSize(SmallTargetWindowsLength + Mib),
            setup.Shrink.MinimumSupported);

        AssertInvariant(5, () => Create(setup.Source, setup.Target, setup.Mappings, bigger));
    }

    [Fact]
    public void Invariant5RejectsAShrunkLengthBelowUsedPlusMargin()
    {
        var setup = ShrinkSetup();
        // Lo ocupado + margen son 50 GiB; 20 GiB no basta aunque cumpla el mínimo de 10 GiB.
        var tooSmall = new SourceShrinkStep(setup.Shrink.Partition, setup.Shrink.OriginalLength, ByteSize.FromGiB(20), ByteSize.FromGiB(10));

        AssertInvariant(5, () => Create(setup.Source, setup.Target, setup.Mappings, tooSmall));
    }

    [Fact]
    public void Invariant5RejectsAShrinkThatNamesAnotherPartition()
    {
        var setup = ShrinkSetup();
        var other = new SourceShrinkStep(SourcePartitionGuid(2), setup.Shrink.OriginalLength, setup.Shrink.ShrunkLength, setup.Shrink.MinimumSupported);

        AssertInvariant(5, () => Create(setup.Source, setup.Target, setup.Mappings, other));
    }

    [Fact]
    public void Invariant5RequiresGrowthWhenTheTargetExceedsTheShrunkLength()
    {
        var setup = ShrinkSetup();
        var mappings = setup.Mappings.ToArray();
        mappings[WindowsIndex] = mappings[WindowsIndex] with { Resize = ResizeKind.None };
        var smallerShrink = new SourceShrinkStep(
            setup.Shrink.Partition,
            setup.Shrink.OriginalLength,
            ByteSize.FromGiB(50),
            setup.Shrink.MinimumSupported);

        AssertInvariant(5, () => Create(setup.Source, setup.Target, mappings, smallerShrink));
    }

    [Fact]
    public void Invariant5RejectsASourceShrinkWhenWindowsDoesNotGetSmaller()
    {
        var source = SystemDisk();
        var target = TargetDisk(size: 256 * Gib);
        var extents = TargetLayoutCalculator.Calculate(source, target).Value;
        var shrink = new SourceShrinkStep(SourcePartitionGuid(3), new ByteSize(SourceWindowsLength), ByteSize.FromGiB(50), ByteSize.FromGiB(45));

        AssertInvariant(5, () => Create(source, target, MappingsFor(source, extents), shrink));
    }

    [Fact]
    public void Invariant4RejectsAShrunkWindowsPartitionWithoutASourceShrink()
    {
        var setup = ShrinkSetup();

        AssertInvariant(4, () => Create(setup.Source, setup.Target, setup.Mappings, null));
    }

    // ----- SourceShrinkStep: sus propias invariantes -----

    [Fact]
    public void AShrinkStepMustReduceTheLength()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            _ = new SourceShrinkStep(SourcePartitionGuid(3), ByteSize.FromGiB(50), ByteSize.FromGiB(50), ByteSize.FromGiB(10));
        });
    }

    [Fact]
    public void AShrinkStepMayNotGoBelowTheMinimumSupportedByWindows()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            _ = new SourceShrinkStep(SourcePartitionGuid(3), ByteSize.FromGiB(50), ByteSize.FromGiB(20), ByteSize.FromGiB(30));
        });
    }

    [Fact]
    public void AShrinkStepLengthMustBeWholeMebibytes()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            _ = new SourceShrinkStep(SourcePartitionGuid(3), ByteSize.FromGiB(50), new ByteSize(ByteSize.FromGiB(50).Bytes - 1), ByteSize.FromGiB(10));
        });
    }

    // ----- Confirm con la confirmación de reducción (FR-013) -----

    [Fact]
    public void ConfirmingAShrinkPlanWithoutTheShrinkConfirmationIsRejected()
    {
        var (plan, target) = ShrinkPlan(ByteSize.FromGiB(45));

        Should.Throw<InvalidOperationException>(() => plan.Confirm(Confirmation(target), target));

        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    [Fact]
    public void ConfirmingWithAShrinkConfirmationForAnotherPartitionIsRejected()
    {
        var (plan, target) = ShrinkPlan(ByteSize.FromGiB(45));
        var wrong = new ShrinkConfirmation(SourcePartitionGuid(2), plan.SourceShrink.ShouldNotBeNull().ShrunkLength, At);

        Should.Throw<InvalidOperationException>(() => plan.Confirm(Confirmation(target), target, wrong));

        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    [Fact]
    public void ConfirmingWithAShrinkConfirmationForAnotherLengthIsRejected()
    {
        var (plan, target) = ShrinkPlan(ByteSize.FromGiB(45));
        var wrong = new ShrinkConfirmation(SourcePartitionGuid(3), ByteSize.FromGiB(60), At);

        Should.Throw<InvalidOperationException>(() => plan.Confirm(Confirmation(target), target, wrong));

        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    [Fact]
    public void ConfirmingAShrinkPlanWithMatchingConfirmationsMovesToConfirmed()
    {
        var (plan, target) = ShrinkPlan(ByteSize.FromGiB(45));
        var shrink = ConfirmationOf(plan);

        plan.Confirm(Confirmation(target), target, shrink);

        plan.Status.ShouldBe(PlanStatus.Confirmed);
        plan.SourceShrinkConfirmation.ShouldBe(shrink);
    }

    [Fact]
    public void AShrinkConfirmationIsRejectedOnAPlanThatDoesNotShrinkTheSource()
    {
        var target = TargetDisk(size: 512 * Gib);
        var plan = PlanFor(InventoryOf(SystemDisk(), target)).Value;
        var shrink = new ShrinkConfirmation(SourcePartitionGuid(3), ByteSize.FromGiB(50), At);

        Should.Throw<InvalidOperationException>(() => plan.Confirm(Confirmation(target), target, shrink));

        plan.Status.ShouldBe(PlanStatus.Draft);
    }

    // ----- Ayudantes -----

    private static (Disk Source, Disk Target, PartitionMapping[] Mappings, SourceShrinkStep Shrink) ShrinkSetup()
    {
        var source = SystemDisk();
        var target = TargetDisk(size: SmallTargetSize);
        var decision = TargetLayoutCalculator.Decide(source, target, CapacityPolicy.Default, ByteSize.FromGiB(45)).Value;
        var shrink = decision.SourceShrink.ShouldNotBeNull();

        return (source, target, MappingsFor(source, decision.Extents, shrink.ShrunkLength), shrink);
    }

    /// <summary>Correspondencias de prueba. La partición de Windows crece si su extensión supera la base (la reducida o la de origen).</summary>
    private static PartitionMapping[] MappingsFor(Disk source, IReadOnlyList<Extent> extents, ByteSize? shrunk = null) =>
        source.Partitions
            .Select((partition, i) => new PartitionMapping(
                partition,
                extents[i],
                new PartitionGuid(G(5000 + i)),
                partition.Volume?.FileSystem == FileSystemKind.Ntfs ? CopyMethod.UsedClustersFromSnapshot : CopyMethod.RawFull,
                partition.Role == PartitionRole.Windows && extents[i].Length > (shrunk ?? new ByteSize(partition.Extent.Length)).Bytes
                    ? ResizeKind.GrowFileSystem
                    : ResizeKind.None))
            .ToArray();

    private static ClonePlan Create(Disk source, Disk target, IReadOnlyList<PartitionMapping> mappings, SourceShrinkStep? shrink) =>
        ClonePlan.Create(
            new PlanId(G(9000)),
            source,
            target,
            InventoryFingerprint.Compute(["huella de prueba"]),
            mappings,
            G(9001),
            VerificationMode.Structural,
            Array.Empty<PreflightCheck>(),
            new CloneEstimate(new ByteSize(1), TimeSpan.Zero, TimeSpan.Zero),
            CapacityPolicy.Default,
            shrink);

    private static TargetConfirmation Confirmation(Disk target) => new(target.Id, TargetConfirmation.ExpectedSuffix(target), At);

    private static void AssertInvariant(int number, Action action)
    {
        var exception = Should.Throw<InvalidOperationException>(action);
        exception.Message.ShouldContain($"Invariante {number}");
    }
}
