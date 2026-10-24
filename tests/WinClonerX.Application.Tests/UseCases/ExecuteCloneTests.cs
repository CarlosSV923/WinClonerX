using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Application.Tests.Fakes;
using WinClonerX.Application.Tests.TestData;
using WinClonerX.Application.UseCases.Execution;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.BootRepair;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Application.Tests.TestData.ExecuteCloneTestData;
using static WinClonerX.Application.Tests.TestData.PlanningTestData;

namespace WinClonerX.Application.Tests.UseCases;

public sealed class ExecuteCloneTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Resultado de cada compensación en el caso de compensación fallida: la primera falla y las demás se ejecutan.</summary>
    private static readonly bool[] FailingCompensationOutcomes = [false, true, true];

    /// <summary>Operaciones de sólo lectura permitidas sobre cada tipo de origen. Cualquier otra sería una escritura en el origen.</summary>
    private static readonly string[] BlockSourceReads = ["GetAllocationAsync", "ReadAsync", "Dispose"];
    private static readonly string[] SnapshotSetOperations = ["OpenVolume", "DisposeAsync"];

    private static readonly ExecutionPhase[] HappyPhases =
    [
        ExecutionPhase.Revalidate,
        ExecutionPhase.Snapshot,
        ExecutionPhase.PrepareTarget,
        ExecutionPhase.CopyData,
        ExecutionPhase.CommitLayout,
        ExecutionPhase.ExpandFileSystem,
        ExecutionPhase.RepairBoot,
        ExecutionPhase.StructuralVerification,
        ExecutionPhase.Cleanup,
    ];

    /// <summary>Fase en la que se inyecta un fallo. El nombre coincide con la fase del dominio.</summary>
    public enum FaultPoint
    {
        Revalidate,
        Snapshot,
        PrepareTarget,
        CopyData,
        CommitLayout,
        ExpandFileSystem,
        RepairBoot,
        StructuralVerification,
    }

    [Fact]
    public async Task HappyPathRunsEveryPhaseInOrder()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        h.Phases().ShouldBe(HappyPhases);
    }

    [Fact]
    public async Task HappyPathWritesOnlyToTheTargetAndNeverMutatesTheSource()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        h.Copier.Copies.All(copy => ReferenceEquals(copy.Target, h.Target)).ShouldBeTrue();
        h.Target.Writes.ShouldNotBeEmpty();
        AssertSourcesOnlyRead(h);
    }

    [Fact]
    public async Task HappyPathWritesStayInsideTheTargetExtentsOfTheirPartitions()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        var extents = h.Plan.Mappings.Select(mapping => mapping.TargetExtent).ToArray();
        h.Target.Writes.All(write => extents.Any(extent =>
            write.Offset >= extent.Offset && write.Offset + write.Length <= extent.End)).ShouldBeTrue();
        h.Copier.Copies.Select(copy => copy.TargetOffset).ToArray()
            .ShouldBe(extents.Select(extent => extent.Offset).ToArray());
    }

    [Fact]
    public async Task CommitLayoutAndBringOnlineRunAfterTheLastDataWrite()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        var calls = h.Target.Calls;
        var lastWrite = calls.LastIndexOf("WriteAsync");
        var commit = calls.IndexOf("CommitLayoutAsync");
        var online = calls.IndexOf("BringOnlineAsync");

        lastWrite.ShouldBeGreaterThan(-1);
        commit.ShouldBeGreaterThan(lastWrite);
        online.ShouldBeGreaterThan(commit);
        h.Target.CommittedLayouts.Single().DiskGuid.ShouldBe(h.Plan.NewDiskGuid);
    }

    [Fact]
    public async Task HappyPathDisposesTheSnapshotAndTheTargetAndReleasesSleepAndMounts()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        h.Snapshot!.Deleted.ShouldBeTrue();
        h.Target.DisposeCount.ShouldBe(1);
        h.Mounter.Mounted.Count.ShouldBe(3);
        h.Mounter.ActiveMounts.ShouldBe(0);
        h.Power.Acquisitions.ShouldBe(1);
        h.Power.Releases.ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task SleepIsBlockedWhileTheCloneRuns()
    {
        var h = new Harness();
        var blockedWhileCopying = false;
        h.Copier.OnChunk = () => blockedWhileCopying = h.Power.IsSleepBlocked;

        await h.RunAsync(Ct);

        blockedWhileCopying.ShouldBeTrue();
    }

    [Fact]
    public async Task HappyPathReportsCompletedWithNextStepsAndVerificationResults()
    {
        var h = new Harness();

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.Structural!.Passed.ShouldBeTrue();
        report.Full.ShouldBeNull();
        report.SourceRestored.ShouldBeNull();
        report.RecoveryEnvironment.ShouldBe(RecoveryEnvironmentState.Configured);
        report.LogFile.ShouldBe(h.Log.CurrentFile);
        report.NextSteps.ShouldBe(new[] { NextStepCode.ShutDownAndReplaceDisk, NextStepCode.SelectBootDeviceInFirmware });
        h.Events.OfType<ExecutionCompleted>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task HappyPathRepairsBootWithTheNewPartitionGuids()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        var windowsGuid = h.Plan.Mappings.Single(mapping => mapping.Source.Role == PartitionRole.Windows).NewUniqueId;
        var espGuid = h.Plan.Mappings.Single(mapping => mapping.Source.Role == PartitionRole.EfiSystem).NewUniqueId;
        var recoveryGuid = h.Plan.Mappings.Single(mapping => mapping.Source.Role == PartitionRole.Recovery).NewUniqueId;

        h.Expander.Expanded.ShouldBe(new[] { windowsGuid });
        h.Registry.Rewrites.Count.ShouldBe(1);
        var rewrite = h.Registry.Rewrites.Single().Rewrite;
        rewrite.DriveLetter.ShouldBe('C');
        rewrite.NewValue.SequenceEqual(MountedDevicesRewrite.GptValue(windowsGuid.Value)).ShouldBeTrue();
        h.Boot.Rebuilds.Count.ShouldBe(1);
        h.Recovery.Configurations.Count.ShouldBe(1);
        h.Mounter.Mounted.ShouldContain(windowsGuid);
        h.Mounter.Mounted.ShouldContain(espGuid);
        h.Mounter.Mounted.ShouldContain(recoveryGuid);
    }

    [Fact]
    public async Task BitLockerWarningAddsTheReenableBitLockerNextStep()
    {
        var h = new Harness(ConfirmedPlan(BitLockerFacts()));

        var report = await h.RunAsync(Ct);

        report.NextSteps.ShouldBe(new[]
        {
            NextStepCode.ShutDownAndReplaceDisk,
            NextStepCode.SelectBootDeviceInFirmware,
            NextStepCode.ReenableBitLocker,
        });
    }

    [Fact]
    public async Task WinReFailureIsAWarningAndDoesNotFailTheClone()
    {
        var h = new Harness();
        h.Recovery.Result = new RecoveryConfigResult(false, "reagentc exited with code 1");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.RecoveryEnvironment.ShouldBe(RecoveryEnvironmentState.Warning);
        h.Mounter.ActiveMounts.ShouldBe(0);
    }

    [Theory]
    [InlineData(FaultPoint.Revalidate, "DisksChanged")]
    [InlineData(FaultPoint.Snapshot, "SnapshotFailed")]
    [InlineData(FaultPoint.PrepareTarget, "TargetInUse")]
    [InlineData(FaultPoint.CopyData, "SourceReadError")]
    [InlineData(FaultPoint.CommitLayout, "TargetWriteError")]
    [InlineData(FaultPoint.ExpandFileSystem, "ExtendFailed")]
    [InlineData(FaultPoint.RepairBoot, "RegistryEditFailed")]
    [InlineData(FaultPoint.StructuralVerification, "VerificationFailed")]
    public async Task FaultInAnyPhaseFailsTheCloneAndCompensatesInOrder(FaultPoint fault, string expectedCode)
    {
        var h = new Harness();
        h.Inject(fault);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.NextSteps.ShouldBe(new[] { NextStepCode.KeepOriginalDiskUntilVerified });

        var failure = h.Events.OfType<ExecutionFailed>().Single().Failure;
        failure.Code.ShouldBe(expectedCode);
        failure.Phase.ShouldBe(Enum.Parse<ExecutionPhase>(fault.ToString()));

        h.Compensations().ShouldBe(ExpectedCompensations(fault));
        h.Events.OfType<CompensationCompleted>().ShouldAllBe(compensation => compensation.Success);
        h.Target.Calls.Contains("DeleteLayoutAsync").ShouldBe(ExpectedCompensations(fault).Contains(CompensationKind.DeleteTargetLayout));
    }

    [Theory]
    [InlineData(FaultPoint.Revalidate)]
    [InlineData(FaultPoint.Snapshot)]
    [InlineData(FaultPoint.PrepareTarget)]
    [InlineData(FaultPoint.CopyData)]
    [InlineData(FaultPoint.CommitLayout)]
    [InlineData(FaultPoint.ExpandFileSystem)]
    [InlineData(FaultPoint.RepairBoot)]
    [InlineData(FaultPoint.StructuralVerification)]
    public async Task FaultInAnyPhaseCleansUpAndNeverWritesToTheSource(FaultPoint fault)
    {
        var h = new Harness();
        h.Inject(fault);

        await h.RunAsync(Ct);

        if (h.Snapshots.CreatedSets.Count > 0)
        {
            h.Snapshot!.Deleted.ShouldBeTrue();
        }

        h.Target.DisposeCount.ShouldBe(h.Targets.OpenedPlans.Count);
        h.Mounter.ActiveMounts.ShouldBe(0);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Power.Acquisitions.ShouldBe(1);
        AssertSourcesOnlyRead(h);
        h.Events.OfType<ExecutionFailed>().Count().ShouldBe(1);
        h.Plan.Status.ShouldBe(fault == FaultPoint.Revalidate ? PlanStatus.Invalidated : PlanStatus.Confirmed);
    }

    [Fact]
    public async Task RevalidationMismatchInvalidatesThePlanAndLogsIt()
    {
        var h = new Harness();
        h.Inject(FaultPoint.Revalidate);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Plan.Status.ShouldBe(PlanStatus.Invalidated);
        h.Plan.InvalidationReason.ShouldBe("DisksChanged");
        h.Snapshots.Calls.ShouldBeEmpty();
        h.Targets.OpenedPlans.ShouldBeEmpty();

        var invalidated = h.Log.Events.Single(e => e.Event == "PlanInvalidated");
        invalidated.Level.ShouldBe(LogLevelKind.Warning);
        invalidated.Properties["Reason"].ShouldBe("DisksChanged");
    }

    [Fact]
    public async Task FailingCompensationIsRecordedAndTheRemainingOnesStillRun()
    {
        var h = new Harness();
        h.Target.FailOn[nameof(ITargetDisk.CommitLayoutAsync)] = new PlatformException(PlatformErrorCode.TargetWriteError, "commit failed");
        h.Target.FailOn[nameof(ITargetDisk.DeleteLayoutAsync)] = new PlatformException(PlatformErrorCode.Unknown, "delete failed");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Events.OfType<CompensationCompleted>().Select(c => c.Success).ToArray().ShouldBe(FailingCompensationOutcomes);
        h.Compensations().ShouldBe(new[] { CompensationKind.DeleteTargetLayout, CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
        h.Snapshot!.Deleted.ShouldBeTrue();
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Events.OfType<ExecutionFailed>().Single().Failure.Code.ShouldBe("TargetWriteError");
    }

    [Theory]
    [InlineData(FaultPoint.Revalidate, "DisksChanged", ExecutionPhase.Revalidate)]
    [InlineData(FaultPoint.Snapshot, "SnapshotFailed", ExecutionPhase.Snapshot)]
    [InlineData(FaultPoint.CopyData, "SourceReadError", ExecutionPhase.CopyData)]
    public async Task AFailedRunCarriesTheFailureCodeAndPhaseInItsReport(FaultPoint fault, string expectedCode, ExecutionPhase expectedPhase)
    {
        var h = new Harness();
        h.Inject(fault);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.Failure.ShouldNotBeNull();
        report.Failure.Code.ShouldBe(expectedCode);
        report.Failure.Phase.ShouldBe(expectedPhase);
        report.Failure.ShouldBe(h.Events.OfType<ExecutionFailed>().Single().Failure);
    }

    [Fact]
    public async Task AnUnexpectedExceptionIsReportedAsUnexpectedWithThePhaseItHappenedIn()
    {
        var h = new Harness();
        h.Copier.FailOn[nameof(IVolumeCopier.CopyAsync)] = new InvalidOperationException("boom");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.Failure.ShouldNotBeNull();
        report.Failure.Code.ShouldBe("Unexpected");
        report.Failure.Phase.ShouldBe(ExecutionPhase.CopyData);
        report.Failure.TechnicalDetail.ShouldContain("boom");
    }

    [Fact]
    public async Task CompletedAndCancelledRunsHaveNoFailureInTheirReport()
    {
        var completed = new Harness();
        using var cts = new CancellationTokenSource();
        var cancelled = new Harness();
        cancelled.Copier.OnChunk = () => cts.Cancel();

        var completedReport = await completed.RunAsync(Ct);
        var cancelledReport = await cancelled.RunAsync(cts.Token);

        completedReport.Outcome.ShouldBe(CloneOutcome.Completed);
        completedReport.Failure.ShouldBeNull();
        cancelledReport.Outcome.ShouldBe(CloneOutcome.Cancelled);
        cancelledReport.Failure.ShouldBeNull();
    }

    [Fact]
    public async Task CancellationDuringCopyDataEndsCancelledAfterCompensations()
    {
        using var cts = new CancellationTokenSource();
        var h = new Harness();
        h.Copier.OnChunk = () => cts.Cancel();

        var report = await h.RunAsync(cts.Token);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        report.NextSteps.ShouldBe(new[] { NextStepCode.KeepOriginalDiskUntilVerified });
        h.Phases().ShouldNotContain(ExecutionPhase.CommitLayout);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
        h.Compensations().ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
        h.Snapshot!.Deleted.ShouldBeTrue();
        h.Target.DisposeCount.ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Events.OfType<ExecutionCancelled>().Count().ShouldBe(1);
        h.Events.OfType<ExecutionFailed>().ShouldBeEmpty();
        h.Log.Events.Any(e => e.Event == "ExecutionCancelled").ShouldBeTrue();
        AssertSourcesOnlyRead(h);
    }

    [Fact]
    public async Task CancellationBeforeTheFirstPhaseOnlyReleasesSleep()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var h = new Harness();

        var report = await h.RunAsync(cts.Token);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        h.Compensations().ShouldBe(new[] { CompensationKind.AllowSleep });
        h.Phases().ShouldBeEmpty();
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task UnconfirmedPlanThrowsBeforeAnyPortIsCalled()
    {
        var h = new Harness(DraftPlan());

        await Should.ThrowAsync<InvalidOperationException>(() => h.RunAsync(Ct));

        AssertNoPortCalled(h);
    }

    [Fact]
    public async Task UnknownPlanThrowsBeforeAnyPortIsCalled()
    {
        var h = new Harness();

        await Should.ThrowAsync<InvalidOperationException>(
            () => h.Orchestrator.ExecuteAsync(new PlanId(Guid.NewGuid()), Ct));

        AssertNoPortCalled(h);
    }

    [Fact]
    public async Task AFullVerificationPlanRunsToCompletionInsteadOfBeingRefused()
    {
        var h = new Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Phases().ShouldContain(ExecutionPhase.FullVerification);
    }

    [Fact]
    public async Task EveryLogEventLeavesTheEnvelopeNamesToTheAdapterAndTheRunIsBracketed()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        h.Log.Events.First().Event.ShouldBe("ExecutionStarted");
        h.Log.Events.Last().Event.ShouldBe("ExecutionCompleted");
        h.Log.Events.SelectMany(e => e.Properties.Keys).Intersect(LogEvent.ReservedPropertyNames).ShouldBeEmpty();
        h.Log.Events.Count(e => e.Event == "PhaseStarted").ShouldBe(HappyPhases.Length);
        h.Log.Events.Count(e => e.Event == "PhaseCompleted").ShouldBe(HappyPhases.Length);
        h.Log.Events.Where(e => e.Event == "PhaseCompleted").All(e => e.Properties.ContainsKey("DurationMs")).ShouldBeTrue();
    }

    [Fact]
    public async Task FailureLogCarriesTheFailureCodePhaseAndTechnicalDetail()
    {
        var h = new Harness();
        h.Inject(FaultPoint.Snapshot);

        await h.RunAsync(Ct);

        var failed = h.Log.Events.Single(e => e.Event == "ExecutionFailed");
        failed.Level.ShouldBe(LogLevelKind.Error);
        failed.Properties["Outcome"].ShouldBe("Failed");
        failed.Properties["FailureCode"].ShouldBe("SnapshotFailed");
        failed.Properties["Phase"].ShouldBe("Snapshot");
        failed.Properties.ContainsKey("TechnicalDetail").ShouldBeTrue();
        failed.Properties.ContainsKey("DurationMs").ShouldBeTrue();
        failed.Properties.Keys.Intersect(LogEvent.ReservedPropertyNames).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(FaultPoint.CopyData)]
    [InlineData(FaultPoint.CommitLayout)]
    public async Task AFailingLogStillRunsTheCompensationsAndTheRunEndsFailed(FaultPoint fault)
    {
        var h = new Harness();
        h.Log.FailOn[nameof(IExecutionLog.Record)] = new InvalidOperationException("log full");
        h.Inject(fault);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Events.OfType<ExecutionFailed>().Single().Failure.Phase.ShouldBe(Enum.Parse<ExecutionPhase>(fault.ToString()));
        h.Compensations().ShouldBe(ExpectedCompensations(fault));
        h.Target.Calls.Contains("DeleteLayoutAsync").ShouldBe(ExpectedCompensations(fault).Contains(CompensationKind.DeleteTargetLayout));
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task AFailingLogDoesNotTurnACompletedCloneIntoAFailure()
    {
        var h = new Harness();
        h.Log.FailOn[nameof(IExecutionLog.Record)] = new InvalidOperationException("log full");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Phases().ShouldBe(HappyPhases);
        h.Events.OfType<ExecutionCompleted>().Count().ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task ProgressIsReportedAtMostEvery500MsAndLoggedAtMostEvery5s()
    {
        var h = new Harness();
        // Cada fragmento avanza el reloj un segundo: 12 fragmentos en total.
        h.Copier.OnChunk = () => h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1);

        await h.RunAsync(Ct);

        var reportedAt = h.Events.OfType<ProgressReported>().Select(e => e.At).ToArray();
        reportedAt.Length.ShouldBeGreaterThan(1);
        for (var i = 0; i < reportedAt.Length - 1; i++)
        {
            (reportedAt[i + 1] - reportedAt[i]).ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMilliseconds(500));
        }

        // Con 12 s de copia, a 0 s, 5 s y 10 s: tres líneas de registro.
        h.Log.Events.Count(e => e.Event == "ProgressReported").ShouldBe(3);
    }

    [Fact]
    public async Task ReportedProgressNeverExceedsTheTotalOfTheCopy()
    {
        var h = new Harness();
        h.Copier.OnChunk = () => h.Clock.UtcNow = h.Clock.UtcNow.AddSeconds(1);

        await h.RunAsync(Ct);

        var progress = h.Events.OfType<ProgressReported>().Select(e => e.Progress).ToArray();
        progress.ShouldNotBeEmpty();
        var total = progress[0].BytesTotal;
        total.ShouldBeGreaterThan(0);
        progress.ShouldAllBe(p => p.BytesTotal == total && p.BytesDone <= total);
        progress.Select(p => p.BytesDone).ShouldBe(progress.Select(p => p.BytesDone).OrderBy(done => done).ToArray());
    }

    [Fact]
    public async Task MountReleaseFailureAfterVerificationStillCompletesWithoutDeletingTheLayout()
    {
        var h = new Harness();
        h.MountFaults.FailReleases = true;

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Target.Calls.ShouldNotContain("DeleteLayoutAsync");
        h.Compensations().ShouldBeEmpty();
        AssertCleanupFailed(h, "ReleaseMounts");
        h.Snapshot!.Deleted.ShouldBeTrue();
        h.Target.DisposeCount.ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Power.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task SnapshotDisposeFailureAfterVerificationStillCompletesWithoutDeletingTheLayout()
    {
        var h = new Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.StructuralVerification })
            {
                h.Snapshot!.FailOn[nameof(ISnapshotSet.DisposeAsync)] = new InvalidOperationException("snapshot busy");
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Target.Calls.ShouldNotContain("DeleteLayoutAsync");
        h.Compensations().ShouldBeEmpty();
        AssertCleanupFailed(h, "DisposeSnapshot");
        h.Mounter.ActiveMounts.ShouldBe(0);
        h.Target.DisposeCount.ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Power.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task TargetDisposeFailureAfterVerificationStillCompletesWithoutDeletingTheLayout()
    {
        var h = new Harness();
        h.Target.FailOn[nameof(ITargetDisk.DisposeAsync)] = new InvalidOperationException("disk busy");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Target.Calls.ShouldNotContain("DeleteLayoutAsync");
        h.Compensations().ShouldBeEmpty();
        AssertCleanupFailed(h, "DisposeTarget");
        h.Snapshot!.Deleted.ShouldBeTrue();
        h.Mounter.ActiveMounts.ShouldBe(0);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Power.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task CopyDataEndsWithAFinalProgressReportThatEqualsTheTotal()
    {
        var h = new Harness();

        await h.RunAsync(Ct);

        // Dos particiones en bruto de 16 KiB, Windows con 12 KiB ocupados y Recovery con 4 KiB: 48 KiB en total.
        var copyEnd = h.Events.FindIndex(e => e is PhaseCompleted { Phase: ExecutionPhase.CopyData });
        var last = h.Events[copyEnd - 1].ShouldBeOfType<ProgressReported>().Progress;
        last.BytesTotal.ShouldBe(48 * 1024);
        last.BytesDone.ShouldBe(last.BytesTotal);
    }

    private static void AssertCleanupFailed(Harness h, string step)
    {
        var warnings = h.Log.Events.Where(e => e.Event == "CleanupFailed" && (string?)e.Properties["Step"] == step).ToArray();
        warnings.ShouldNotBeEmpty();
        warnings.ShouldAllBe(w => w.Level == LogLevelKind.Warning
            && !w.Properties.Keys.Any(key => LogEvent.ReservedPropertyNames.Contains(key)));
        h.Events.OfType<ExecutionFailed>().ShouldBeEmpty();
    }

    private static void AssertSourcesOnlyRead(Harness h)
    {
        var blockSources = h.Copier.Copies.Select(copy => copy.Source).OfType<FakeBlockSource>().ToArray();
        blockSources.SelectMany(source => source.Calls).Distinct().ShouldAllBe(call => BlockSourceReads.Contains(call));
        h.Snapshots.CreatedSets.SelectMany(set => set.Calls).Distinct().ShouldAllBe(call => SnapshotSetOperations.Contains(call));
        h.Raw.Calls.Distinct().ShouldAllBe(call => call == "Open");
        h.Target.Calls.ShouldNotContain("ReadAsync");
    }

    private static void AssertNoPortCalled(Harness h)
    {
        h.Inventory.Calls.ShouldBeEmpty();
        h.Snapshots.Calls.ShouldBeEmpty();
        h.Raw.Calls.ShouldBeEmpty();
        h.Targets.Calls.ShouldBeEmpty();
        h.Copier.Calls.ShouldBeEmpty();
        h.Expander.Calls.ShouldBeEmpty();
        h.Mounter.Calls.ShouldBeEmpty();
        h.Registry.Calls.ShouldBeEmpty();
        h.Boot.Calls.ShouldBeEmpty();
        h.Recovery.Calls.ShouldBeEmpty();
        h.Verifier.Calls.ShouldBeEmpty();
        h.Power.Acquisitions.ShouldBe(0);
        h.Log.Events.ShouldBeEmpty();
        h.Events.ShouldBeEmpty();
    }

    private static CompensationKind[] ExpectedCompensations(FaultPoint fault) => fault switch
    {
        FaultPoint.Revalidate => [CompensationKind.AllowSleep],
        FaultPoint.Snapshot or FaultPoint.PrepareTarget or FaultPoint.CopyData =>
            [CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep],
        FaultPoint.CommitLayout or FaultPoint.ExpandFileSystem =>
            [CompensationKind.DeleteTargetLayout, CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep],
        FaultPoint.RepairBoot or FaultPoint.StructuralVerification =>
            [CompensationKind.DeleteTargetLayout, CompensationKind.DeleteSnapshot, CompensationKind.ReleaseMounts, CompensationKind.AllowSleep],
        _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
    };

    /// <summary>Dependencias del orquestador con sus fakes, cableadas como en la aplicación.</summary>
    internal sealed class Harness
    {
        public Harness(ClonePlan? plan = null, DiskInventory? inventory = null)
        {
            Plan = plan ?? ConfirmedPlan();
            Store.Save(Plan);
            Inventory.Inventory = inventory ?? StandardInventory();

            Snapshots.VolumesToServe[WindowsSource] = WindowsVolume();
            Snapshots.VolumesToServe[RecoverySource] = RecoveryVolume();
            Raw.Data = new byte[4 * ChunkSize];

            MountFaults = new ReleaseFaultMounter(Mounter);
            Journal = new FakeSourceRestoreJournal(Resizer);
            Orchestrator = new ExecuteClone(
                Store, Inventory, Snapshots, Raw, Targets, Copier, TargetVerifier, Expander, MountFaults, Registry, Boot, Recovery,
                Verifier, Resizer, Journal, Power, RemovalMonitor, Log, Clock);
            Orchestrator.Events.Subscribe(new EventCollector(Events, e => OnEvent(e)));
        }

        /// <summary>Montajes con fallo de liberación opcional; es el que recibe el orquestador.</summary>
        public ReleaseFaultMounter MountFaults { get; }

        /// <summary>Acción que se ejecuta con cada evento publicado, para armar fallos a mitad de la ejecución.</summary>
        public Action<ExecutionEvent> OnEvent { get; set; } = _ => { };

        public FakeDiskInventoryReader Inventory { get; } = new();

        public FakeSnapshotService Snapshots { get; } = new();

        public FakeRawPartitionReader Raw { get; } = new();

        public FakeTargetDiskFactory Targets { get; } = new();

        public FakeVolumeCopier Copier { get; } = new();

        public FakeTargetVerifier TargetVerifier { get; } = new();

        public FakeFileSystemExpander Expander { get; } = new();

        public FakeTemporaryMounter Mounter { get; } = new();

        public FakeOfflineRegistryEditor Registry { get; } = new();

        public FakeBootConfigurator Boot { get; } = new();

        public FakeRecoveryConfigurator Recovery { get; } = new();

        public FakeStructuralVerifier Verifier { get; } = new();

        public FakeSourceResizer Resizer { get; } = new();

        public FakeSourceRestoreJournal Journal { get; }

        public FakePowerManager Power { get; } = new();

        public FakeDeviceRemovalMonitor RemovalMonitor { get; } = new();

        public FakeExecutionLog Log { get; } = new();

        public FakeClock Clock { get; } = new();

        public ClonePlanStore Store { get; } = new();

        public ClonePlan Plan { get; }

        public List<ExecutionEvent> Events { get; } = [];

        public ExecuteClone Orchestrator { get; }

        public FakeTargetDisk Target => Targets.Disk;

        public FakeSnapshotSet? Snapshot => Snapshots.CreatedSets.LastOrDefault();

        public Task<CloneResultReport> RunAsync(CancellationToken ct) => Orchestrator.ExecuteAsync(Plan.Id, ct);

        public ExecutionPhase[] Phases() => Events.OfType<PhaseStarted>().Select(e => e.Phase).ToArray();

        public CompensationKind[] Compensations() => Events.OfType<CompensationCompleted>().Select(c => c.Kind).ToArray();

        /// <summary>Inyecta el fallo indicado en el fake que corresponde a la fase.</summary>
        public void Inject(FaultPoint fault)
        {
            switch (fault)
            {
                case FaultPoint.Revalidate:
                    // Entre la planificación y la ejecución aparece una partición de datos en el destino.
                    Inventory.Inventory = InventoryOf(SystemDisk(), TargetDisk(withDataPartition: true));
                    break;
                case FaultPoint.Snapshot:
                    Snapshots.FailOn[nameof(ISnapshotService.CreateAsync)] = new PlatformException(PlatformErrorCode.SnapshotFailed, "vss");
                    break;
                case FaultPoint.PrepareTarget:
                    Targets.Disk.FailOn[nameof(ITargetDisk.LockAndTakeOfflineAsync)] = new PlatformException(PlatformErrorCode.TargetInUse, "locked");
                    break;
                case FaultPoint.CopyData:
                    Copier.FailOn[nameof(IVolumeCopier.CopyAsync)] = new PlatformException(PlatformErrorCode.SourceReadError, "read");
                    break;
                case FaultPoint.CommitLayout:
                    Targets.Disk.FailOn[nameof(ITargetDisk.CommitLayoutAsync)] = new PlatformException(PlatformErrorCode.TargetWriteError, "commit");
                    break;
                case FaultPoint.ExpandFileSystem:
                    Expander.FailOn[nameof(IFileSystemExpander.ExpandToPartitionAsync)] = new PlatformException(PlatformErrorCode.ExtendFailed, "extend");
                    break;
                case FaultPoint.RepairBoot:
                    // Falla después de montar las tres particiones, para comprobar que se liberan.
                    Registry.FailOn[nameof(IOfflineRegistryEditor.RewriteMountedDevicesAsync)] = new PlatformException(PlatformErrorCode.RegistryEditFailed, "registry");
                    break;
                case FaultPoint.StructuralVerification:
                    Verifier.Result = new VerificationResult(false, ["tabla GPT distinta del plan"]);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
            }
        }

        private static FakeBlockSource WindowsVolume() => new(
            new byte[4 * ChunkSize],
            new VolumeAllocation(new ByteSize(ChunkSize), [new Extent(0, ChunkSize), new Extent(2 * ChunkSize, 2 * ChunkSize)]));

        private static FakeBlockSource RecoveryVolume() => new(
            new byte[4 * ChunkSize],
            new VolumeAllocation(new ByteSize(ChunkSize), [new Extent(0, ChunkSize)]));
    }

    /// <summary>Envuelve el fake de montajes para poder hacer fallar la liberación de un montaje.</summary>
    internal sealed class ReleaseFaultMounter(FakeTemporaryMounter inner) : ITemporaryMounter
    {
        /// <summary>Si es verdadero, liberar cualquier montaje lanza una excepción.</summary>
        public bool FailReleases { get; set; }

        public async Task<TemporaryMount> MountAsync(PartitionGuid partition, CancellationToken ct)
        {
            var mount = await inner.MountAsync(partition, ct);
            return new TemporaryMount(mount.Root, new ReleaseHandle(this, mount.Release));
        }

        private sealed class ReleaseHandle(ReleaseFaultMounter owner, IAsyncDisposable release) : IAsyncDisposable
        {
            public async ValueTask DisposeAsync()
            {
                if (owner.FailReleases)
                {
                    throw new PlatformException(PlatformErrorCode.MountFailed, "el punto de montaje está en uso");
                }

                await release.DisposeAsync();
            }
        }
    }

    /// <summary>Observador que guarda los eventos publicados por el orquestador y avisa de cada uno.</summary>
    private sealed class EventCollector(List<ExecutionEvent> sink, Action<ExecutionEvent> onEvent) : IObserver<ExecutionEvent>
    {
        public void OnNext(ExecutionEvent value)
        {
            sink.Add(value);
            onEvent(value);
        }

        public void OnError(Exception error)
        {
        }

        public void OnCompleted()
        {
        }
    }
}
