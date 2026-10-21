using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

/// <summary>Flujo de cancelación de la interfaz: confirmación, estados del paso Progreso, cierre de la ventana y resultado (T099).</summary>
public sealed class CancelFlowTests
{
    private static readonly ExecutionId Run = new(Guid.Parse("00000000-0000-0000-0000-0000000000e2"));
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly PlanId Plan = new(Guid.Parse("00000000-0000-0000-0000-0000000000a2"));

    [Fact]
    public async Task CancelAsksForConfirmationAndDoesNothingWhenDeclined()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, confirmation) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        vm.TryRequestCancel().ShouldBeFalse();

        confirmation.Questions.Count.ShouldBe(1);
        confirmation.Questions[0].Message.ShouldBe(Loc.Get("Cancel_Confirm_Message"));
        confirmation.Questions[0].ConfirmText.ShouldBe(Loc.Get("Cancel_Confirm_Yes"));
        confirmation.Questions[0].DeclineText.ShouldBe(Loc.Get("Cancel_Confirm_No"));
        execute.CancelRequests.ShouldBe(0);
        vm.IsCancelAvailable.ShouldBeTrue();
        vm.IsCancelRequested.ShouldBeFalse();
        vm.CancelStatusText.ShouldBe(string.Empty);

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public async Task ConfirmedCancelRequestsTheCancelAndDisablesTheButton()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, confirmation) = Create();
        confirmation.Answer = true;
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        vm.CancelCommand.CanExecute(null).ShouldBeTrue();
        vm.CancelCommand.Execute(null);

        execute.CancelRequests.ShouldBe(1);
        vm.IsCancelRequested.ShouldBeTrue();
        vm.IsCancelAvailable.ShouldBeFalse();
        vm.CancelCommand.CanExecute(null).ShouldBeFalse();
        vm.CancelStatusText.ShouldBe(Loc.Get("Progress_Cancelling"));
        vm.HasCancelStatus.ShouldBeTrue();

        execute.Completion.SetResult(Report(CloneOutcome.Cancelled));
        await run;
    }

    [Fact]
    public async Task CancelDuringAnInterruptiblePhaseShowsCancelling()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, confirmation) = Create();
        confirmation.Answer = true;
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);
        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.CopyData));

        vm.TryRequestCancel().ShouldBeTrue();

        vm.CancelStatusText.ShouldBe(Loc.Get("Progress_Cancelling"));

        execute.Completion.SetResult(Report(CloneOutcome.Cancelled));
        await run;
    }

    [Fact]
    public async Task CancelDuringANonInterruptiblePhaseShowsTheSafeStepUntilItEnds()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, confirmation) = Create();
        confirmation.Answer = true;
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);
        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.CommitLayout));

        vm.TryRequestCancel().ShouldBeTrue();

        vm.CancelStatusText.ShouldBe(Loc.Get("Progress_Cancelling_SafeStep"));

        execute.Publish(new PhaseCompleted(Run, Now, ExecutionPhase.CommitLayout, TimeSpan.FromSeconds(2)));

        vm.CancelStatusText.ShouldBe(Loc.Get("Progress_Cancelling"));

        execute.Completion.SetResult(Report(CloneOutcome.Cancelled));
        await run;
    }

    [Fact]
    public async Task CancelIsNotAvailableOnceTheStructuralVerificationPassed()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, confirmation) = Create();
        confirmation.Answer = true;
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);
        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.StructuralVerification));
        execute.Publish(new PhaseCompleted(Run, Now, ExecutionPhase.StructuralVerification, TimeSpan.FromSeconds(1)));
        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.Cleanup));

        vm.IsCancelAvailable.ShouldBeFalse();
        vm.TryRequestCancel().ShouldBeFalse();

        confirmation.Questions.ShouldBeEmpty();
        execute.CancelRequests.ShouldBe(0);
        vm.CancelStatusText.ShouldBe(string.Empty);

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public async Task CancelIsNotAvailableAfterTheRunEnds()
    {
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        execute.Completion.SetResult(Report());
        await run;

        vm.IsCancelAvailable.ShouldBeFalse();
        vm.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public void TryRequestCancelBeforeAnyRunDoesNothing()
    {
        var (vm, execute, confirmation) = Create();

        vm.TryRequestCancel().ShouldBeFalse();

        confirmation.Questions.ShouldBeEmpty();
        execute.CancelRequests.ShouldBe(0);
    }

    [Fact]
    public async Task ClosingTheWindowDuringARunAsksTheSameConfirmationAndCancelsWhenConfirmed()
    {
        using var _ = new EnglishCulture();
        var (wizard, progress, execute, confirmation) = CreateWizard();
        confirmation.Answer = true;
        var run = progress.StartAsync(Plan, TestContext.Current.CancellationToken);

        wizard.IsExecutionRunning.ShouldBeTrue();
        wizard.RequestCancelForClose().ShouldBeTrue();

        confirmation.Questions.Count.ShouldBe(1);
        execute.CancelRequests.ShouldBe(1);

        execute.Completion.SetResult(Report(CloneOutcome.Cancelled));
        await run;
        wizard.IsExecutionRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task ClosingTheWindowDuringARunStaysOpenWhenDeclined()
    {
        using var _ = new EnglishCulture();
        var (wizard, progress, execute, confirmation) = CreateWizard();
        var run = progress.StartAsync(Plan, TestContext.Current.CancellationToken);

        wizard.RequestCancelForClose().ShouldBeFalse();

        confirmation.Questions.Count.ShouldBe(1);
        execute.CancelRequests.ShouldBe(0);
        progress.IsCancelAvailable.ShouldBeTrue();

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public async Task ClosingTheWindowAfterVerificationDoesNotAskAndStaysOpen()
    {
        using var _ = new EnglishCulture();
        var (wizard, progress, execute, confirmation) = CreateWizard();
        confirmation.Answer = true;
        var run = progress.StartAsync(Plan, TestContext.Current.CancellationToken);
        execute.Publish(new PhaseCompleted(Run, Now, ExecutionPhase.StructuralVerification, TimeSpan.FromSeconds(1)));

        wizard.RequestCancelForClose().ShouldBeFalse();

        confirmation.Questions.ShouldBeEmpty();
        execute.CancelRequests.ShouldBe(0);

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public void ClosingTheWindowWhenNoRunIsInProgressDoesNotRequestAnything()
    {
        var (wizard, _, execute, confirmation) = CreateWizard();

        wizard.IsExecutionRunning.ShouldBeFalse();
        confirmation.Questions.ShouldBeEmpty();
        execute.CancelRequests.ShouldBe(0);
    }

    [Fact]
    public void CancelledResultSaysTheTargetIsUninitializedAndTheOriginalWasNotModified()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Report(CloneOutcome.Cancelled, sourceRestored: null));

        vm.IsFailure.ShouldBeTrue();
        vm.Title.ShouldBe(Loc.Get("Result_Cancelled_Title"));
        vm.Body.ShouldBe(Loc.Get("Result_Cancelled_Body") + " " + Loc.Get("Result_Cancelled_SourceUntouched"));
        vm.Reason.ShouldBe(Loc.Get("Error_Cancelled"));
        vm.IsSourceRestored.ShouldBeFalse();
    }

    [Fact]
    public void CancelledResultOfAShrunkSourceReportsTheRestoredSize()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Report(CloneOutcome.Cancelled, sourceRestored: true));

        vm.Body.ShouldBe(Loc.Get("Result_Cancelled_Body"));
        vm.IsSourceRestored.ShouldBeTrue();
        vm.SourceRestoreText.ShouldBe(Loc.Get("Result_SourceRestored"));
    }

    [Fact]
    public void FailedResultKeepsItsOwnBody()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Report(CloneOutcome.Failed, sourceRestored: null));

        vm.Title.ShouldBe(Loc.Get("Result_Failed_Title"));
        vm.Body.ShouldBe(Loc.Get("Result_Failed_Body"));
    }

    private static (ProgressStepViewModel Vm, FakeExecuteClone Execute, FakeConfirmationService Confirmation) Create()
    {
        var execute = new FakeExecuteClone();
        var confirmation = new FakeConfirmationService();
        return (new ProgressStepViewModel(execute, new TestUiDispatcher(), confirmation), execute, confirmation);
    }

    /// <summary>Wizard with its five pages. The run is started on the progress page directly, which is what the wizard shows.</summary>
    private static (WizardViewModel Wizard, ProgressStepViewModel Progress, FakeExecuteClone Execute, FakeConfirmationService Confirmation) CreateWizard()
    {
        var inventory = AppFixtures.InventoryOf(AppFixtures.SystemDisk(), AppFixtures.TargetDisk());
        var disks = new DisksStepViewModel(new FakeGetInventoryAndEligibility(AppFixtures.Overview(inventory)));
        var review = new ReviewStepViewModel(new FakeBuildClonePlan(target => AppFixtures.PlanFor(inventory, target)));
        var confirm = new ConfirmStepViewModel(new FakeConfirmClonePlan(AppFixtures.PlanFor(inventory, AppFixtures.TargetId).Value));
        var execute = new FakeExecuteClone();
        var confirmation = new FakeConfirmationService();
        var progress = new ProgressStepViewModel(execute, new TestUiDispatcher(), confirmation);
        var wizard = new WizardViewModel(disks, review, confirm, progress, ResultStepFactory.Create());

        return (wizard, progress, execute, confirmation);
    }

    private static CloneResultReport Report(CloneOutcome outcome = CloneOutcome.Completed, bool? sourceRestored = null) => new(
        outcome,
        null,
        null,
        sourceRestored,
        null,
        TimeSpan.FromMinutes(3),
        "log.clef",
        [NextStepCode.KeepOriginalDiskUntilVerified]);
}
