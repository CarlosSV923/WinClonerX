using Shouldly;
using WinClonerX.App.Localization;
using WinClonerX.App.Presentation;
using WinClonerX.App.Tests.Fakes;
using WinClonerX.App.ViewModels;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;
using Xunit;

namespace WinClonerX.App.Tests.ViewModels;

public sealed class ResultStepViewModelTests
{
    [Fact]
    public void StartOverIsAvailableOnlyForCancelledAndFailedOutcomes()
    {
        var vm = ResultStepFactory.Create();

        vm.StartOverCommand.CanExecute(null).ShouldBeFalse();
        vm.Show(Failed(CloneOutcome.Cancelled));
        vm.StartOverCommand.CanExecute(null).ShouldBeTrue();
        vm.Show(Failed(CloneOutcome.Failed));
        vm.StartOverCommand.CanExecute(null).ShouldBeTrue();
        vm.Show(Success());
        vm.StartOverCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void CloseAppIsAvailableOnlyForCompletedAndCallsLifetime()
    {
        var lifetime = new FakeAppLifetime();
        var vm = new ResultStepViewModel(
            new FakeExportLog(), new FakeFileDialogService(), new FakeFolderOpener(), new FixedClock(), lifetime);

        vm.CloseAppCommand.CanExecute(null).ShouldBeFalse();
        vm.Show(Success());
        vm.CloseAppCommand.CanExecute(null).ShouldBeTrue();
        vm.CloseAppCommand.Execute(null);
        lifetime.ShutdownCalls.ShouldBe(1);
        vm.Show(Failed(CloneOutcome.Failed));
        vm.CloseAppCommand.CanExecute(null).ShouldBeFalse();
    }

    [Fact]
    public void SuccessWithWinRecoveryConfiguredShowsTheConfiguredState()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

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
        var vm = ResultStepFactory.Create();

        vm.Show(Success(RecoveryEnvironmentState.Warning));

        vm.IsRecoveryWarning.ShouldBeTrue();
        vm.RecoveryText.ShouldBe(Loc.Get("Result_Recovery_Warning"));
        vm.RecoveryText.ShouldContain("reagentc /enable");
        vm.RecoveryText.ShouldContain("Windows will still start");
    }

    [Fact]
    public void SuccessWithoutRecoveryInformationHidesTheRecoveryLine()
    {
        var vm = ResultStepFactory.Create();

        vm.Show(Success(recovery: null));

        vm.HasRecovery.ShouldBeFalse();
    }

    [Fact]
    public void SuccessShowsAPassedVerificationWithoutIssues()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Success(RecoveryEnvironmentState.Configured));

        vm.HasVerification.ShouldBeTrue();
        vm.VerificationSummary.ShouldBe(Loc.Get("Result_Verification_Passed"));
        vm.VerificationIssues.ShouldBeEmpty();
    }

    [Fact]
    public void SuccessListsTheVerificationIssuesWhenThereAreSome()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();
        var structural = new VerificationResult(false, ["The BCD file is missing"]);

        vm.Show(Success(RecoveryEnvironmentState.Configured, structural));

        vm.VerificationSummary.ShouldBe(Loc.Get("Result_Verification_Failed"));
        vm.VerificationIssues.ShouldBe(["The BCD file is missing"]);
    }

    [Fact]
    public void SuccessShowsTheNextStepsInOrderWithTheirInstructions()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

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
        var vm = ResultStepFactory.Create();

        vm.Show(Success(RecoveryEnvironmentState.Configured, duration: TimeSpan.FromMinutes(45)));

        vm.DurationLine.ShouldBe(Loc.Format("Result_Duration", DurationText.Format(TimeSpan.FromMinutes(45))));
    }

    [Fact]
    public void FailedMinimalVariantShowsTheGenericReasonAndKeepOriginalStep()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

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
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Failed, Failure("TargetWriteError", ExecutionPhase.CopyData)));

        vm.Reason.ShouldBe(Loc.Get("Error_TargetWriteError"));
    }

    [Fact]
    public void MountFailedInRepairBootShowsItsMessageAndTheCodeAndPhaseLine()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Failed, Failure("MountFailed", ExecutionPhase.RepairBoot)));

        vm.Reason.ShouldBe(Loc.Get("Error_MountFailed"));
        vm.HasFailureDetail.ShouldBeTrue();
        vm.FailureDetail.ShouldBe("Code: MountFailed · Phase: " + Loc.Get("Phase_RepairBoot"));
    }

    [Theory]
    [InlineData("Unexpected", "Error_Unexpected")]
    [InlineData("DisksChanged", "Error_DisksChanged")]
    [InlineData("ShrinkInsufficient", "Error_ShrinkInsufficient")]
    [InlineData("ShrinkFailed", "Error_ShrinkFailed")]
    public void UseCaseCodesHaveTheirOwnMessage(string code, string key)
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Failed, Failure(code, ExecutionPhase.Snapshot)));

        vm.Reason.ShouldBe(Loc.Get(key));
        vm.Reason.ShouldNotBe(Loc.Get("Result_Failed_Generic"));
    }

    [Fact]
    public void FailedWithAnUnknownCodeFallsBackToTheGenericReason()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Failed, Failure("SomethingNew", ExecutionPhase.Cleanup)));

        vm.Reason.ShouldBe(Loc.Get("Result_Failed_Generic"));
        vm.FailureDetail.ShouldBe("Code: SomethingNew · Phase: " + Loc.Get("Phase_Cleanup"));
    }

    [Fact]
    public void FailedWithoutAFailureHasNoCodeLineButStillShowsTheGenericReason()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Failed));

        vm.Reason.ShouldBe(Loc.Get("Result_Failed_Generic"));
        vm.HasFailureDetail.ShouldBeFalse();
        vm.FailureDetail.ShouldBeEmpty();
    }

    [Fact]
    public void FailedShowsTheLogFileOfTheRun()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Failed, Failure("MountFailed", ExecutionPhase.RepairBoot)));

        vm.HasLogFile.ShouldBeTrue();
        vm.LogFileText.ShouldBe(Loc.Format("Result_LogFile", "log.clef"));
    }

    [Fact]
    public void CancelledHasNoCodeLineAndKeepsTheLogFile()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Cancelled));

        vm.HasFailureDetail.ShouldBeFalse();
        vm.HasLogFile.ShouldBeTrue();
    }

    [Fact]
    public void SuccessHasNoFailureLines()
    {
        var vm = ResultStepFactory.Create();

        vm.Show(Success(RecoveryEnvironmentState.Configured));

        vm.HasFailureDetail.ShouldBeFalse();
        vm.HasLogFile.ShouldBeFalse();
    }

    [Fact]
    public void CancelledUsesTheCancelledTitleAndReason()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Failed(CloneOutcome.Cancelled));

        vm.IsFailure.ShouldBeTrue();
        vm.Title.ShouldBe(Loc.Get("Result_Cancelled_Title"));
        vm.Reason.ShouldBe(Loc.Get("Error_Cancelled"));
    }

    [Fact]
    public void ShowReplacesThePreviousReport()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();
        vm.Show(Success(RecoveryEnvironmentState.Warning, new VerificationResult(false, ["Problem"])));

        vm.Show(Failed(CloneOutcome.Failed));

        vm.HasVerification.ShouldBeFalse();
        vm.VerificationIssues.ShouldBeEmpty();
        vm.HasRecovery.ShouldBeFalse();
        vm.NextSteps.ShouldBe([Loc.Get("Result_Failed_KeepOriginal")]);
    }

    [Fact]
    public void APassedFullVerificationShowsTheMatchLineForASuccessfulRun()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();

        vm.Show(Success(RecoveryEnvironmentState.Configured) with { Full = new VerificationResult(true, [], 0, []) });

        vm.HasFullVerification.ShouldBeTrue();
        vm.FullVerificationSummary.ShouldBe(Loc.Get("Result_FullVerification_Passed"));
        vm.FullVerificationIssues.ShouldBeEmpty();
    }

    [Fact]
    public void AFailedFullVerificationShowsLocalizedPartitionSummariesAndUsbHint()
    {
        using var _ = new EnglishCulture();
        var vm = ResultStepFactory.Create();
        var full = new VerificationResult(
            false,
            ["Partition {1} (Windows): 3 bytes differ; first at target disk offsets 4096"],
            3,
            [4096],
            [new VerificationPartitionSummary(PartitionRole.Windows, 1024L * 1024 * 1024, 3L * 1024 * 1024)]);

        vm.Show(Failed(CloneOutcome.Failed, Failure("VerificationFailed", ExecutionPhase.FullVerification)) with { Full = full });

        vm.HasFullVerification.ShouldBeTrue();
        vm.FullVerificationSummary.ShouldBe(Loc.Get("Result_FullVerification_FailedSummary"));
        vm.FullVerificationIssues.ShouldBe([
            Loc.Format("Result_FullVerification_PartitionMismatch", "Windows", SizeText.Format(new ByteSize(3L * 1024 * 1024)), SizeText.Format(new ByteSize(1024L * 1024 * 1024)))]);
        vm.FullVerificationHint.ShouldBe(Loc.Get("Result_FullVerification_UsbHint"));
        vm.HasFullVerificationHint.ShouldBeTrue();
        vm.IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void FailedFullVerificationSummaryIsLocalizedInSpanish()
    {
        using var _ = new CultureScope("es");
        var vm = ResultStepFactory.Create();
        var full = new VerificationResult(false, [], 1, [], [new VerificationPartitionSummary(PartitionRole.Windows, 1024, 1)]);

        vm.Show(Failed(CloneOutcome.Failed, Failure("VerificationFailed", ExecutionPhase.FullVerification)) with { Full = full });

        vm.FullVerificationIssues.Single().ShouldBe("Windows: 1 B distintos de 1 KiB comparados");
        vm.FullVerificationHint.ShouldBe("Si el disco de destino está conectado por USB, prueba a conectarlo por dentro (SATA) o con otro adaptador y vuelve a intentarlo: algunos adaptadores USB devuelven datos erróneos al leer.");
    }

    [Fact]
    public void WithoutAFullVerificationTheLineIsHidden()
    {
        var vm = ResultStepFactory.Create();

        vm.Show(Success(RecoveryEnvironmentState.Configured));

        vm.HasFullVerification.ShouldBeFalse();
        vm.FullVerificationSummary.ShouldBeEmpty();
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

    private static CloneResultReport Failed(CloneOutcome outcome, ExecutionFailure? failure = null) => new(
        outcome,
        null,
        null,
        null,
        null,
        TimeSpan.FromMinutes(3),
        "log.clef",
        [NextStepCode.KeepOriginalDiskUntilVerified],
        failure);

    private static ExecutionFailure Failure(string code, ExecutionPhase phase) => new(code, phase, "technical detail");
}
