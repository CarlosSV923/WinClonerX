using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;
using static WinClonerX.Domain.Tests.Planning.ShrinkFixtures;

namespace WinClonerX.Domain.Tests.Planning;

public sealed class PreflightEvaluatorShrinkTests
{
    [Fact]
    public void ATargetThatNeedsShrinkingIsEligibleWithTheShrinkStrategy()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(size: SmallTargetSize)), FactsWithMinimum(ByteSize.FromGiB(45)));

        var target = targets.Single();
        target.IsEligible.ShouldBeTrue();
        target.Strategy.ShouldBe(SizeStrategy.ShrinkSource);
        target.Shortfall.ShouldBeNull();
        target.Reasons.ShouldBeEmpty();
    }

    [Fact]
    public void TheShrinkStrategyAddsNoWarningCodeOfItsOwn()
    {
        var (_, targets) = Evaluate(
            InventoryOf(SystemDisk(), TargetDisk(size: SmallTargetSize, withDataPartition: true)),
            FactsWithMinimum(ByteSize.FromGiB(45)));

        var target = targets.Single();
        target.IsEligible.ShouldBeTrue();
        target.Reasons.Select(reason => reason.Code).ShouldBe([PreflightCode.TargetNotEmpty]);
    }

    [Fact]
    public void WithoutAKnownMinimumTheSmallerTargetIsStillTooSmall()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(size: SmallTargetSize)), FactsWithMinimum(null));

        var target = targets.Single();
        target.IsEligible.ShouldBeFalse();
        target.Strategy.ShouldBeNull();
        var check = target.Reasons.Single(reason => reason.Code == PreflightCode.TargetTooSmall);
        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Shortfall"].ShouldBe(ByteSize.FromGiB(56));
    }

    [Fact]
    public void AMinimumThatDoesNotFitBlocksWithShrinkInsufficientAndTheShortfall()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(size: SmallTargetSize)), FactsWithMinimum(ByteSize.FromGiB(201)));

        var target = targets.Single();
        target.IsEligible.ShouldBeFalse();
        target.Strategy.ShouldBeNull();
        var check = target.Reasons.Single(reason => reason.Code == PreflightCode.ShrinkInsufficient);
        check.Severity.ShouldBe(Severity.Blocking);
        check.Facts["Shortfall"].ShouldBe(ByteSize.FromGiB(1) + ByteSize.FromMiB(868));
        target.Shortfall.ShouldBe(ByteSize.FromGiB(1) + ByteSize.FromMiB(868));
        target.Reasons.Any(reason => reason.Code == PreflightCode.TargetTooSmall).ShouldBeFalse();
    }

    [Fact]
    public void AMarginLimitedShortfallStaysTargetTooSmall()
    {
        var (_, targets) = Evaluate(
            InventoryOf(SystemDisk(windowsUsed: 150 * Gib), TargetDisk(size: 160 * Gib)),
            FactsWithMinimum(ByteSize.FromGiB(10)));

        var target = targets.Single();
        target.IsEligible.ShouldBeFalse();
        target.Reasons.Single(reason => reason.Code == PreflightCode.TargetTooSmall).Severity.ShouldBe(Severity.Blocking);
        target.Reasons.Any(reason => reason.Code == PreflightCode.ShrinkInsufficient).ShouldBeFalse();
    }

    [Fact]
    public void ASameOrLargerTargetKeepsTheSameOrLargerStrategyWithAKnownMinimum()
    {
        var (_, targets) = Evaluate(InventoryOf(SystemDisk(), TargetDisk(size: 512 * Gib)), FactsWithMinimum(ByteSize.FromGiB(45)));

        targets.Single().Strategy.ShouldBe(SizeStrategy.SameOrLarger);
    }

    private static (PreflightReport Global, IReadOnlyList<TargetEligibility> Targets) Evaluate(DiskInventory inventory, PlatformFacts facts) =>
        PreflightEvaluator.Evaluate(inventory, facts, CapacityPolicy.Default);
}
