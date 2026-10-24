using Shouldly;
using WinClonerX.App.Localization;
using WinClonerX.App.Presentation;
using WinClonerX.App.Tests.Fakes;
using WinClonerX.App.ViewModels;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using Xunit;

namespace WinClonerX.App.Tests.ViewModels;

/// <summary>
/// US2 UI: the temporary shrink of the source disk (FR-009, FR-013, FR-021a), from the hint on step 1 to the restore state on
/// step 5. The plans are built by the real domain planner on a destination smaller than the source's Windows partition.
/// </summary>
public sealed class ShrinkFlowTests
{
    private const string Suffix = "AB7F";

    [Fact]
    public async Task TheDisksStepSaysWindowsMustBeShrunkFirstForASmallerTarget()
    {
        using var _ = new EnglishCulture();
        var disks = await LoadDisks(ShrinkInventory(), AppFixtures.ShrinkFacts());

        var target = disks.Targets.Single();
        target.IsSelectable.ShouldBeTrue();
        target.StrategyText.ShouldBe("The Windows partition on the source disk must be shrunk temporarily");
        target.StrategyText.ShouldBe(Loc.Get("Disks_Strategy_Shrink"));
        target.HelpText.ShouldContain("The Windows partition on the source disk must be shrunk temporarily");
    }

    [Fact]
    public async Task TheDisksStepShowsTheShrinkHintInSpanishToo()
    {
        using var _ = new CultureScope("es-ES");
        var disks = await LoadDisks(ShrinkInventory(), AppFixtures.ShrinkFacts());

        disks.Targets.Single().StrategyText.ShouldBe("Se reducirá temporalmente la partición de Windows del disco de origen");
    }

    [Fact]
    public async Task TheDisksStepKeepsTheExtendHintForALargerTarget()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var disks = await LoadDisks(inventory, AppFixtures.Facts());

        disks.Targets.Single().StrategyText.ShouldBe("The Windows partition does not need to be shrunk");
    }

    [Fact]
    public async Task TheReviewStepShowsTheTemporaryShrinkOfTheSource()
    {
        using var _ = new EnglishCulture();
        var review = await LoadReview(ShrinkInventory(), AppFixtures.ShrinkFacts());

        review.IsFailed.ShouldBeFalse();
        review.HasSourceShrink.ShouldBeTrue();
        var shrink = review.Plan!.SourceShrink!;
        review.SourceShrinkText.ShouldBe(Loc.Format(
            "Shrink_Review_Body",
            SizeText.Format(shrink.OriginalLength),
            SizeText.Format(shrink.ShrunkLength)));
        review.SourceShrinkText.ShouldContain("The Windows partition on the source disk will go from");
        review.SourceShrinkText.ShouldContain(SizeText.Format(shrink.ShrunkLength));
    }

    [Fact]
    public async Task TheReviewStepHasNoShrinkBlockWhenTheSourceIsNotReduced()
    {
        using var _ = new EnglishCulture();
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var review = await LoadReview(inventory, AppFixtures.Facts());

        review.HasPlan.ShouldBeTrue();
        review.HasSourceShrink.ShouldBeFalse();
        review.SourceShrinkText.ShouldBeEmpty();
    }

    [Fact]
    public void CloneNowNeedsBothTheSerialAndTheShrinkAuthorization()
    {
        using var _ = new EnglishCulture();
        var (target, plan) = ShrinkPlan();
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.HasSourceShrink.ShouldBeTrue();
        vm.CloneNowCommand.CanExecute(null).ShouldBeFalse();

        vm.TypedSuffix = Suffix;
        vm.IsSuffixValid.ShouldBeTrue();
        vm.IsReadyToClone.ShouldBeFalse("the serial alone does not authorize the shrink");
        vm.CloneNowCommand.CanExecute(null).ShouldBeFalse();

        vm.ConfirmSourceShrink = true;
        vm.IsReadyToClone.ShouldBeTrue();
        vm.CloneNowCommand.CanExecute(null).ShouldBeTrue();

        vm.TypedSuffix = string.Empty;
        vm.CloneNowCommand.CanExecute(null).ShouldBeFalse("the shrink alone does not confirm the disk");
    }

    [Fact]
    public void TheShrinkPanelExplainsTheSizesAndThatTheSourceIsRestored()
    {
        using var _ = new EnglishCulture();
        var (target, plan) = ShrinkPlan();
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));

        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        var shrink = plan.SourceShrink!;
        vm.SourceShrinkText.ShouldBe(Loc.Format(
            "Shrink_Confirm_Body",
            SizeText.Format(shrink.OriginalLength),
            SizeText.Format(shrink.ShrunkLength)));
        vm.SourceShrinkText.ShouldContain(SizeText.Format(shrink.ShrunkLength));
        vm.SourceShrinkText.ShouldContain("Windows may take several minutes");
    }

    [Fact]
    public async Task ConfirmingAShrinkPlanSendsTheShrinkAuthorization()
    {
        var (target, plan) = ShrinkPlan();
        var confirm = new FakeConfirmClonePlan(plan);
        var vm = new ConfirmStepViewModel(confirm);
        vm.Prepare(target, plan, new HashSet<PreflightCode>());
        vm.TypedSuffix = Suffix;
        vm.ConfirmSourceShrink = true;

        await vm.CloneNowCommand.ExecuteAsync(null);

        confirm.CallCount.ShouldBe(1);
        confirm.LastConfirmSourceShrink.ShouldBe(true);
    }

    [Fact]
    public void PrepareClearsTheShrinkAuthorizationOfAnEarlierVisit()
    {
        var (target, plan) = ShrinkPlan();
        var vm = new ConfirmStepViewModel(new FakeConfirmClonePlan(plan));
        vm.Prepare(target, plan, new HashSet<PreflightCode>());
        vm.ConfirmSourceShrink = true;

        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.ConfirmSourceShrink.ShouldBeFalse();
        vm.IsReadyToClone.ShouldBeFalse();
    }

    [Fact]
    public async Task ConfirmingANonShrinkPlanHidesThePanelAndSendsNoShrinkAuthorization()
    {
        using var _ = new EnglishCulture();
        var target = AppFixtures.TargetDisk(serial: "SN-AB7F");
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), target);
        var plan = AppFixtures.PlanFor(inventory, target.Id).Value;
        var confirm = new FakeConfirmClonePlan(plan);
        var vm = new ConfirmStepViewModel(confirm);
        vm.Prepare(target, plan, new HashSet<PreflightCode>());

        vm.HasSourceShrink.ShouldBeFalse();
        vm.SourceShrinkText.ShouldBeEmpty();
        vm.ConfirmSourceShrink.ShouldBeFalse();

        vm.TypedSuffix = Suffix;
        vm.IsReadyToClone.ShouldBeTrue("without a shrink the serial is the only condition");

        await vm.CloneNowCommand.ExecuteAsync(null);

        confirm.LastConfirmSourceShrink.ShouldBe(false);
    }

    [Fact]
    public void AShrunkSourceThatWasRestoredSaysSo()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Report(sourceRestored: true));

        vm.IsSourceRestored.ShouldBeTrue();
        vm.IsSourceRestoreWarning.ShouldBeFalse();
        vm.SourceRestoreText.ShouldBe("The Windows partition on the source disk went back to its original size.");
        vm.SourceRestoreText.ShouldBe(Loc.Get("Result_SourceRestored"));
    }

    [Fact]
    public void AShrunkSourceThatWasNotRestoredShowsAWarningWithTheManualFix()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Report(sourceRestored: false));

        vm.IsSourceRestored.ShouldBeFalse();
        vm.IsSourceRestoreWarning.ShouldBeTrue();
        vm.SourceRestoreText.ShouldBe(Loc.Get("Result_SourceRestore_Failed"));
        vm.SourceRestoreText.ShouldContain("The clone is valid");
        vm.SourceRestoreText.ShouldContain("Disk Management");
        vm.SourceRestoreText.ShouldContain("Extend Volume");
    }

    [Fact]
    public void WithoutAShrinkThereIsNoRestoreStateAtAll()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();
        vm.Show(Report(sourceRestored: false));

        vm.Show(Report(sourceRestored: null));

        vm.IsSourceRestored.ShouldBeFalse();
        vm.IsSourceRestoreWarning.ShouldBeFalse();
        vm.SourceRestoreText.ShouldBeEmpty();
    }

    private static DiskInventory ShrinkInventory() =>
        AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.ShrinkTargetDisk());

    private static (Disk Target, ClonePlan Plan) ShrinkPlan()
    {
        var target = AppFixtures.ShrinkTargetDisk(serial: "SN-AB7F");
        var plan = AppFixtures.PlanFor(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), target), target.Id, AppFixtures.ShrinkFacts()).Value;
        return (target, plan);
    }

    private static async Task<DisksStepViewModel> LoadDisks(DiskInventory inventory, PlatformFacts facts)
    {
        var disks = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory, facts)));
        await disks.LoadAsync(TestContext.Current.CancellationToken);
        return disks;
    }

    private static async Task<ReviewStepViewModel> LoadReview(DiskInventory inventory, PlatformFacts facts)
    {
        var review = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target, facts)), new FakeInfoDialogService());
        await review.LoadAsync(AppFixtures.TargetId, TestContext.Current.CancellationToken);
        return review;
    }

    private static CloneResultReport Report(bool? sourceRestored) => new(
        CloneOutcome.Completed,
        new VerificationResult(true, []),
        null,
        sourceRestored,
        RecoveryEnvironmentState.Configured,
        TimeSpan.FromMinutes(45),
        "log.clef",
        [NextStepCode.ShutDownAndReplaceDisk]);
}
