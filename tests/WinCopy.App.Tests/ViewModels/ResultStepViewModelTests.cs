using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Execution;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

public sealed class ResultStepViewModelTests
{
    [Fact]
    public void SuccessWithWinRecoveryConfiguredShowsTheConfiguredState()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Success(RecoveryEnvironmentState.Configured));

        vm.IsSuccess.ShouldBeTrue();
        vm.IsFailure.ShouldBeFalse();
        vm.Title.ShouldBe(Loc.Get("Result_Success_Title"));
        vm.HasRecovery.ShouldBeTrue();
        vm.RecoveryText.ShouldBe(Loc.Get("Result_Recovery_Configured"));
        vm.IsRecoveryWarning.ShouldBeFalse();
    }

    [Fact]
    public void SuccessWithWinRecoveryWarningExplainsTheHintAndThatWindowsStillBoots()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Success(RecoveryEnvironmentState.Warning));

        vm.IsRecoveryWarning.ShouldBeTrue();
        vm.RecoveryText.ShouldBe(Loc.Get("Result_Recovery_Warning"));
        vm.RecoveryText.ShouldContain("reagentc /enable");
        vm.RecoveryText.ShouldContain("Windows will still start");
    }

    [Fact]
    public void SuccessWithoutRecoveryInformationHidesTheRecoveryLine()
    {
        var vm = new ResultStepViewModel();

        vm.Show(Success(recovery: null));

        vm.HasRecovery.ShouldBeFalse();
    }

    [Fact]
    public void SuccessShowsAPassedVerificationWithoutIssues()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Success(RecoveryEnvironmentState.Configured));

        vm.HasVerification.ShouldBeTrue();
        vm.VerificationSummary.ShouldBe(Loc.Get("Result_Verification_Passed"));
        vm.VerificationIssues.ShouldBeEmpty();
    }

    [Fact]
    public void SuccessListsTheVerificationIssuesWhenThereAreSome()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();
        var structural = new VerificationResult(false, ["The BCD file is missing"]);

        vm.Show(Success(RecoveryEnvironmentState.Configured, structural));

        vm.VerificationSummary.ShouldBe(Loc.Get("Result_Verification_Failed"));
        vm.VerificationIssues.ShouldBe(["The BCD file is missing"]);
    }

    [Fact]
    public void SuccessShowsTheNextStepsInOrderWithTheirInstructions()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Success(
            RecoveryEnvironmentState.Configured,
            nextSteps:
            [
                NextStepCode.ShutDownAndReplaceDisk,
                NextStepCode.SelectBootDeviceInFirmware,
                NextStepCode.ReenableBitLocker,
            ]));

        vm.NextSteps.ShouldBe(
        [
            Loc.Get("NextStep_ShutDownAndReplaceDisk"),
            Loc.Get("NextStep_SelectBootDeviceInFirmware"),
            Loc.Get("NextStep_ReenableBitLocker"),
        ]);
        vm.NextSteps[0].ShouldContain("Shut down");
        vm.NextSteps[1].ShouldContain("boot menu");
        vm.NextSteps[2].ShouldContain("BitLocker");
    }

    [Fact]
    public void SuccessShowsTheDuration()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Success(RecoveryEnvironmentState.Configured, duration: TimeSpan.FromMinutes(45)));

        vm.DurationLine.ShouldBe(Loc.Format("Result_Duration", DurationText.Format(TimeSpan.FromMinutes(45))));
    }

    [Fact]
    public void FailedMinimalVariantShowsTheGenericReasonAndKeepOriginalStep()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Failed(CloneOutcome.Failed));

        vm.IsSuccess.ShouldBeFalse();
        vm.IsFailure.ShouldBeTrue();
        vm.Title.ShouldBe(Loc.Get("Result_Failed_Title"));
        vm.Body.ShouldBe(Loc.Get("Result_Failed_Body"));
        vm.Reason.ShouldBe(Loc.Get("Result_Failed_Generic"));
        vm.NextSteps.ShouldBe([Loc.Get("Result_Failed_KeepOriginal")]);
        vm.HasVerification.ShouldBeFalse();
        vm.HasRecovery.ShouldBeFalse();
    }

    [Fact]
    public void FailedWithAKnownErrorCodeShowsItsMessage()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Failed(CloneOutcome.Failed), "TargetWriteError");

        vm.Reason.ShouldBe(Loc.Get("Error_TargetWriteError"));
    }

    [Fact]
    public void FailedWithAnUnknownCodeFallsBackToTheGenericReason()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Failed(CloneOutcome.Failed), "DisksChanged");

        vm.Reason.ShouldBe(Loc.Get("Result_Failed_Generic"));
    }

    [Fact]
    public void CancelledUsesTheCancelledTitleAndReason()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();

        vm.Show(Failed(CloneOutcome.Cancelled));

        vm.IsFailure.ShouldBeTrue();
        vm.Title.ShouldBe(Loc.Get("Result_Cancelled_Title"));
        vm.Reason.ShouldBe(Loc.Get("Error_Cancelled"));
    }

    [Fact]
    public void ExportLogIsNotAvailableYet()
    {
        var vm = new ResultStepViewModel();

        vm.Show(Success(RecoveryEnvironmentState.Configured));

        vm.IsExportLogAvailable.ShouldBeFalse();
    }

    [Fact]
    public void ShowReplacesThePreviousReport()
    {
        using var _ = new EnglishCulture();
        var vm = new ResultStepViewModel();
        vm.Show(Success(RecoveryEnvironmentState.Warning, new VerificationResult(false, ["Problem"])));

        vm.Show(Failed(CloneOutcome.Failed));

        vm.HasVerification.ShouldBeFalse();
        vm.VerificationIssues.ShouldBeEmpty();
        vm.HasRecovery.ShouldBeFalse();
        vm.NextSteps.ShouldBe([Loc.Get("Result_Failed_KeepOriginal")]);
    }

    private static CloneResultReport Success(
        RecoveryEnvironmentState? recovery = RecoveryEnvironmentState.Configured,
        VerificationResult? structural = null,
        IReadOnlyList<NextStepCode>? nextSteps = null,
        TimeSpan? duration = null) => new(
        CloneOutcome.Completed,
        structural ?? new VerificationResult(true, []),
        null,
        null,
        recovery,
        duration ?? TimeSpan.FromMinutes(30),
        "log.clef",
        nextSteps ?? [NextStepCode.ShutDownAndReplaceDisk]);

    private static CloneResultReport Failed(CloneOutcome outcome) => new(
        outcome,
        null,
        null,
        null,
        null,
        TimeSpan.FromMinutes(3),
        "log.clef",
        [NextStepCode.KeepOriginalDiskUntilVerified]);
}
