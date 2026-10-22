using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Inventory;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

public sealed class WizardStepWiringTests
{
    private static readonly string[] RestartReadOrder = ["recovery", "inventory"];
    [Fact]
    public async Task NextOnDisksIsEnabledOnlyAfterAnEligibleSelection()
    {
        var (wizard, disks, _) = Create(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()));
        await disks.LoadAsync(TestContext.Current.CancellationToken);

        wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Disks);
        wizard.IsCurrentStepValid.ShouldBeFalse();
        wizard.NextCommand.CanExecute(null).ShouldBeFalse();

        disks.SelectTargetCommand.Execute(disks.Targets.Single());

        wizard.IsCurrentStepValid.ShouldBeTrue();
        wizard.NextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task NextFromDisksBuildsThePlanAndShowsTheReviewPage()
    {
        var (wizard, disks, review) = Create(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()));
        await disks.LoadAsync(TestContext.Current.CancellationToken);
        disks.SelectTargetCommand.Execute(disks.Targets.Single());

        await wizard.NextCommand.ExecuteAsync(null);

        wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Review);
        wizard.CurrentPage.ShouldBeSameAs(review);
        review.HasPlan.ShouldBeTrue();
        review.IsValid.ShouldBeTrue();
        wizard.IsCurrentStepValid.ShouldBeTrue();
    }

    [Fact]
    public async Task NextOnReviewWaitsForTheWarningsToBeAccepted()
    {
        var inventory = AppFixtures.InventoryOf(
            AppFixtures.SystemDisk(windowsEncryption: EncryptionState.BitLockerOn),
            AppFixtures.TargetDisk());
        var (wizard, disks, review) = Create(inventory);
        await disks.LoadAsync(TestContext.Current.CancellationToken);
        disks.SelectTargetCommand.Execute(disks.Targets.Single());
        await wizard.NextCommand.ExecuteAsync(null);

        wizard.IsCurrentStepValid.ShouldBeFalse();
        wizard.NextCommand.CanExecute(null).ShouldBeFalse();

        review.Warnings.Single().IsAccepted = true;

        wizard.IsCurrentStepValid.ShouldBeTrue();
        wizard.NextCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task GoingBackToDisksKeepsTheSelectionAndRebuildsThePlanOnNext()
    {
        var builder = new FakeBuildClonePlan(target => AppFixtures.PlanFor(
            AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk()), target));
        var inventory = new FakeGetInventoryAndEligibility(
            AppFixtures.Overview(AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk())));
        var disks = new DisksStepViewModel(inventory);
        var review = new ReviewStepViewModel(builder);
        var wizard = new WizardViewModel(disks, review);
        await disks.LoadAsync(TestContext.Current.CancellationToken);
        disks.SelectTargetCommand.Execute(disks.Targets.Single());
        await wizard.NextCommand.ExecuteAsync(null);

        wizard.BackCommand.Execute(null);

        wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Disks);
        disks.IsValid.ShouldBeTrue();
        wizard.IsCurrentStepValid.ShouldBeTrue();

        await wizard.NextCommand.ExecuteAsync(null);

        builder.CallCount.ShouldBe(2);
        review.HasPlan.ShouldBeTrue();
    }

    [Fact]
    public void ShellWithoutPagesKeepsTheOriginalBehavior()
    {
        var wizard = new WizardViewModel();

        wizard.DisksStep.ShouldBeNull();
        wizard.ReviewStep.ShouldBeNull();
        wizard.CurrentPage.ShouldBeSameAs(wizard.CurrentStep);
    }

    [Fact]
    public async Task NextFromReviewShowsTheConfirmPageWithTheTargetDisk()
    {
        using var _ = new EnglishCulture();
        var h = await CreateAtReview(TestContext.Current.CancellationToken);

        await h.Wizard.NextCommand.ExecuteAsync(null);

        h.Wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Confirm);
        h.Wizard.CurrentPage.ShouldBeSameAs(h.Confirm);
        h.Confirm.TargetCard!.Model.ShouldBe(AppFixtures.TargetDisk().Model);
        h.Confirm.PromptText.ShouldBe(Loc.Format("Confirm_Prompt", Loc.Get("Confirm_Source_Serial"), "TGT-0002"));
        h.Wizard.IsCurrentStepValid.ShouldBeFalse();
        // Step 3 has its own action: the generic Next button is hidden and disabled.
        h.Wizard.IsNextVisible.ShouldBeFalse();
        h.Wizard.NextCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public async Task TypingTheSuffixValidatesStepThreeAndEnablesOnlyCloneNow()
    {
        var h = await CreateAtReview(TestContext.Current.CancellationToken);
        await h.Wizard.NextCommand.ExecuteAsync(null);

        h.Confirm.TypedSuffix = "0002";

        h.Wizard.IsCurrentStepValid.ShouldBeTrue();
        h.Wizard.NextCommand.CanExecute(null).ShouldBeFalse();
        h.Confirm.CloneNowCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task CloneNowRunsTheCloneAndEndsOnTheResultPage()
    {
        var h = await CreateAtReview(TestContext.Current.CancellationToken);
        await h.Wizard.NextCommand.ExecuteAsync(null);
        h.Confirm.TypedSuffix = "0002";

        await h.Confirm.CloneNowCommand.ExecuteAsync(null);

        h.Wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Progress);
        h.Wizard.CurrentPage.ShouldBeSameAs(h.Progress);
        h.Wizard.IsNextVisible.ShouldBeFalse();
        h.Wizard.BackCommand.CanExecute(null).ShouldBeFalse();
        h.Execute.LastPlanId.ShouldBe(h.Review.Plan!.Id);
        h.ConfirmPlan.LastTypedSuffix.ShouldBe("0002");

        h.Execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.CopyData));
        h.Progress.Phases.Count.ShouldBe(1);

        var report = Report();
        h.Execute.Completion.SetResult(report);
        await h.Wizard.ExecutionCompletion;

        h.Wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Result);
        h.Wizard.CurrentPage.ShouldBeSameAs(h.Result);
        h.Result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task APlanThatChangedReturnsToTheDisksStepWithANotice()
    {
        using var _ = new EnglishCulture();
        var h = await CreateAtReview(TestContext.Current.CancellationToken);
        await h.Wizard.NextCommand.ExecuteAsync(null);
        h.Confirm.TypedSuffix = "0002";
        h.ConfirmPlan.NextFailure = new PlanInvalidatedException(h.Review.Plan!.Id, "DisksChanged");

        await h.Confirm.CloneNowCommand.ExecuteAsync(null);

        h.Wizard.CurrentIndex.ShouldBe(0);
        h.Wizard.CurrentPage.ShouldBeSameAs(h.Disks);
        h.Wizard.HasNotice.ShouldBeTrue();
        h.Wizard.Notice.ShouldBe(Loc.Get("Confirm_PlanInvalidated"));
        h.Review.HasPlan.ShouldBeFalse();
        h.Inventory.CallCount.ShouldBe(2, "the disks are read again");
        h.Execute.CallCount.ShouldBe(0);
    }

    [Fact]
    public async Task BackFromConfirmReturnsToReviewWithItsPlan()
    {
        var h = await CreateAtReview(TestContext.Current.CancellationToken);
        await h.Wizard.NextCommand.ExecuteAsync(null);

        h.Wizard.BackCommand.Execute(null);

        h.Wizard.CurrentPage.ShouldBeSameAs(h.Review);
        h.Wizard.IsCurrentStepValid.ShouldBeTrue();
        h.Review.HasPlan.ShouldBeTrue();
    }

    [Fact]
    public async Task ResultStepIsReachedWithTheReportOfTheRun()
    {
        var h = await CreateAtReview(TestContext.Current.CancellationToken);
        await h.Wizard.NextCommand.ExecuteAsync(null);
        h.Confirm.TypedSuffix = "0002";
        await h.Confirm.CloneNowCommand.ExecuteAsync(null);

        h.Execute.Completion.SetResult(Report(CloneOutcome.Failed));
        await h.Wizard.ExecutionCompletion;

        h.Wizard.CurrentPage.ShouldBeSameAs(h.Result);
        h.Result.IsFailure.ShouldBeTrue();
        h.Result.IsSuccess.ShouldBeFalse();
    }

    [Fact]
    public async Task StartingOverClearsThePreviousRunAndAllowsANewRun()
    {
        var order = new List<string>();
        var recovery = new FakeRecoverPendingSourceRestore { OnCall = () => order.Add("recovery") };
        var h = await CreateAtReview(TestContext.Current.CancellationToken, recovery: recovery, order: order);
        await h.Wizard.NextCommand.ExecuteAsync(null);
        h.Confirm.TypedSuffix = "0002";
        await h.Confirm.CloneNowCommand.ExecuteAsync(null);
        h.Execute.Completion.SetResult(Report(CloneOutcome.Failed));
        await h.Wizard.ExecutionCompletion;

        h.Result.StartOverCommand.CanExecute(null).ShouldBeTrue();
        await h.Wizard.StartOverCommand.ExecuteAsync(null);

        h.Wizard.CurrentIndex.ShouldBe(0);
        recovery.CallCount.ShouldBe(2);
        order.TakeLast(2).ShouldBe(RestartReadOrder);
        h.Wizard.Notice.ShouldBeNull();
        h.Inventory.CallCount.ShouldBe(2);
        h.Disks.SelectedTarget.ShouldBeNull();
        h.Review.HasPlan.ShouldBeFalse();
        h.Confirm.TypedSuffix.ShouldBeEmpty();
        h.Confirm.ConfirmSourceShrink.ShouldBeFalse();
        h.Progress.Phases.ShouldBeEmpty();
        h.Progress.IsRunning.ShouldBeFalse();
        h.Result.Outcome.ShouldBeNull();
        h.Result.HasExportStatus.ShouldBeFalse();
        h.Wizard.StepItems.ShouldAllBe(step => !step.IsCompleted);

        h.Disks.SelectTargetCommand.Execute(h.Disks.Targets.Single());
        await h.Wizard.NextCommand.ExecuteAsync(null);
        await h.Wizard.NextCommand.ExecuteAsync(null);
        h.Confirm.TypedSuffix = "0002";
        await h.Confirm.CloneNowCommand.ExecuteAsync(null);
        h.Execute.Completion.SetResult(Report() with { LogFile = "new-run.clef" });
        await h.Wizard.ExecutionCompletion;

        h.Wizard.CurrentIndex.ShouldBe(4);
        h.Result.IsSuccess.ShouldBeTrue();
        h.Result.LogFile.ShouldBe("new-run.clef");
        h.Execute.CallCount.ShouldBe(2);
        h.Execute.ActiveObserverCount.ShouldBe(0);
    }

    [Fact]
    public async Task AnUnexpectedExceptionFromTheRunStillEndsOnAFailedResultPage()
    {
        using var _ = new EnglishCulture();
        var h = await CreateAtReview(TestContext.Current.CancellationToken);
        await h.Wizard.NextCommand.ExecuteAsync(null);
        h.Confirm.TypedSuffix = "0002";
        await h.Confirm.CloneNowCommand.ExecuteAsync(null);

        h.Execute.Completion.SetException(new ArgumentException("unexpected", "logEvent"));
        await h.Wizard.ExecutionCompletion;

        h.Wizard.CurrentStep.Kind.ShouldBe(WizardStepKind.Result);
        h.Wizard.CurrentPage.ShouldBeSameAs(h.Result);
        h.Result.IsFailure.ShouldBeTrue();
        h.Result.IsSuccess.ShouldBeFalse();
        h.Result.Reason.ShouldBe(Loc.Get("Error_Unexpected"));
        h.Result.FailureDetail.ShouldBe("Code: Unexpected · Phase: " + Loc.Get("Phase_Revalidate"));
    }

    [Fact]
    public async Task TheResultShowsTheFailureFromTheReportEvenWhenTheEventsNeverReachTheUi()
    {
        using var _ = new EnglishCulture();
        // The dispatcher keeps the posted work and never runs it: the progress events never reach the UI before the run ends.
        var h = await CreateAtReview(TestContext.Current.CancellationToken, deferredEvents: true);
        await h.Wizard.NextCommand.ExecuteAsync(null);
        h.Confirm.TypedSuffix = "0002";
        await h.Confirm.CloneNowCommand.ExecuteAsync(null);
        var failure = new ExecutionFailure("MountFailed", ExecutionPhase.RepairBoot, "mount");

        h.Execute.Publish(new ExecutionFailed(Run, Now, failure));
        h.Execute.Completion.SetResult(Report(CloneOutcome.Failed, failure));
        await h.Wizard.ExecutionCompletion;

        h.Result.Reason.ShouldBe(Loc.Get("Error_MountFailed"));
        h.Result.FailureDetail.ShouldBe("Code: MountFailed · Phase: " + Loc.Get("Phase_RepairBoot"));
    }

    private static readonly ExecutionId Run = new(Guid.Parse("00000000-0000-0000-0000-0000000000e1"));
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);

    private static CloneResultReport Report(CloneOutcome outcome = CloneOutcome.Completed, ExecutionFailure? failure = null) => new(
        outcome,
        new VerificationResult(true, []),
        null,
        null,
        RecoveryEnvironmentState.Configured,
        TimeSpan.FromMinutes(30),
        "log.clef",
        [NextStepCode.ShutDownAndReplaceDisk],
        failure);

    private sealed record Harness(
        WizardViewModel Wizard,
        DisksStepViewModel Disks,
        ReviewStepViewModel Review,
        ConfirmStepViewModel Confirm,
        ProgressStepViewModel Progress,
        ResultStepViewModel Result,
        FakeExecuteClone Execute,
        FakeConfirmClonePlan ConfirmPlan,
        FakeGetInventoryAndEligibility Inventory);

    /// <summary>
    /// Full five-step wizard on a fresh system disk and a blank USB target, with the disks already selected and loaded.
    /// With <paramref name="deferredEvents"/> the progress page never gets to run the events it is given.
    /// </summary>
    private static async Task<Harness> CreateAtReview(
        CancellationToken ct,
        bool deferredEvents = false,
        FakeRecoverPendingSourceRestore? recovery = null,
        List<string>? order = null)
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var inventoryFake = new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory));
        inventoryFake.OnCall = () => order?.Add("inventory");
        var disks = new DisksStepViewModel(inventoryFake, recovery);
        var review = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));
        var plan = AppFixtures.PlanFor(inventory, AppFixtures.TargetId).Value;
        var confirmPlan = new FakeConfirmClonePlan(plan);
        var confirm = new ConfirmStepViewModel(confirmPlan);
        var execute = new FakeExecuteClone();
        var progress = new ProgressStepViewModel(execute, new TestUiDispatcher(deferredEvents), new FakeConfirmationService());
        var result = ResultStepFactory.Create();
        var wizard = new WizardViewModel(disks, review, confirm, progress, result);

        await disks.LoadAsync(ct);
        disks.SelectTargetCommand.Execute(disks.Targets.Single());
        await wizard.NextCommand.ExecuteAsync(null);

        return new Harness(wizard, disks, review, confirm, progress, result, execute, confirmPlan, inventoryFake);
    }

    private static (WizardViewModel Wizard, DisksStepViewModel Disks, ReviewStepViewModel Review) Create(DiskInventory inventory)
    {
        var disks = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory)));
        var review = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));
        return (new WizardViewModel(disks, review), disks, review);
    }
}
