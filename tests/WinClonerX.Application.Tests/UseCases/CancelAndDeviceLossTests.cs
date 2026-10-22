using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Application.Tests.TestData;
using WinClonerX.Domain.Execution;
using Xunit;
using static WinClonerX.Application.Tests.TestData.ShrinkTestData;

namespace WinClonerX.Application.Tests.UseCases;

/// <summary>
/// Desconexión de un disco durante la ejecución (US3, escenario 2) y lectura defectuosa del origen (escenario 3). Un disco
/// retirado aborta el clonado como fallo, con su código específico; las compensaciones se ejecutan después y no ocultan la causa.
/// Usa el mismo arnés que <see cref="ExecuteCloneTests"/>, con <see cref="FakeDeviceRemovalMonitor"/> como vigilancia.
/// </summary>
public sealed class CancelAndDeviceLossTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompensationKind[] CopyCompensations = [CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep];

    [Fact]
    public async Task SourceRemovedDuringCopyAbortsWithItsOwnFailureCode()
    {
        var h = new ExecuteCloneTests.Harness();
        var removed = false;
        h.Copier.OnChunk = () =>
        {
            if (!removed)
            {
                removed = true;
                h.RemovalMonitor.Remove(h.Plan.Source.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        var failure = FailureOf(h);
        failure.Code.ShouldBe(nameof(PlatformErrorCode.SourceDeviceRemoved));
        failure.Phase.ShouldBe(ExecutionPhase.CopyData);
        h.Phases().ShouldBe([ExecutionPhase.Revalidate, ExecutionPhase.Snapshot, ExecutionPhase.PrepareTarget, ExecutionPhase.CopyData]);
        h.Compensations().ShouldBe(CopyCompensations);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
        h.Events.OfType<ExecutionCancelled>().ShouldBeEmpty();
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task TargetRemovedDuringCopyAbortsWithItsOwnFailureCode()
    {
        var h = new ExecuteCloneTests.Harness();
        var removed = false;
        h.Copier.OnChunk = () =>
        {
            if (!removed)
            {
                removed = true;
                h.RemovalMonitor.Remove(h.Plan.Target.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        var failure = FailureOf(h);
        failure.Code.ShouldBe(nameof(PlatformErrorCode.TargetDeviceRemoved));
        failure.Phase.ShouldBe(ExecutionPhase.CopyData);
        h.Compensations().ShouldBe(CopyCompensations);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
    }

    [Fact]
    public async Task TargetRemovedDuringCommitLayoutWaitsForThePhaseToFinish()
    {
        var h = new ExecuteCloneTests.Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.CommitLayout })
            {
                h.RemovalMonitor.Remove(h.Plan.Target.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        // CommitLayout no es interrumpible: el diseño y la puesta en línea se completan antes de abortar.
        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Target.Calls.ShouldContain("CommitLayoutAsync");
        h.Target.Calls.ShouldContain("BringOnlineAsync");
        h.Events.OfType<PhaseCompleted>().Select(e => e.Phase).ShouldContain(ExecutionPhase.CommitLayout);
        h.Phases().ShouldNotContain(ExecutionPhase.ExpandFileSystem);
        h.Phases().ShouldNotContain(ExecutionPhase.RepairBoot);

        var failure = FailureOf(h);
        failure.Code.ShouldBe(nameof(PlatformErrorCode.TargetDeviceRemoved));
        failure.Phase.ShouldBe(ExecutionPhase.CommitLayout);
        h.Compensations().ShouldBe([CompensationKind.DeleteTargetLayout, CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep]);
    }

    [Fact]
    public async Task SourceRemovedBeforeAnyPhaseStartsAbortsBeforeTheCopy()
    {
        var h = new ExecuteCloneTests.Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.Revalidate })
            {
                h.RemovalMonitor.Remove(h.Plan.Source.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        FailureOf(h).Code.ShouldBe(nameof(PlatformErrorCode.SourceDeviceRemoved));
        h.Phases().ShouldBe([ExecutionPhase.Revalidate]);
        h.Snapshots.Calls.ShouldBeEmpty();
        h.Copier.Calls.ShouldBeEmpty();
        h.Compensations().ShouldBe([CompensationKind.AllowSleep]);
    }

    [Fact]
    public async Task TargetRemovedDuringShrinkWaitsForTheShrinkAndStillRestoresTheSource()
    {
        var h = ShrinkHarness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.ShrinkSource })
            {
                h.RemovalMonitor.Remove(h.Plan.Target.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        // ShrinkSource no es interrumpible: la reducción termina, y la restauración devuelve el origen a su tamaño.
        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Resizer.Shrinks.Count.ShouldBe(1);
        h.Phases().ShouldBe([ExecutionPhase.Revalidate, ExecutionPhase.ShrinkSource]);
        FailureOf(h).Code.ShouldBe(nameof(PlatformErrorCode.TargetDeviceRemoved));
        h.Compensations().ShouldBe([CompensationKind.RestoreSource, CompensationKind.AllowSleep]);
        h.Resizer.Restores.Count.ShouldBe(1);
        report.SourceRestored.ShouldBe(true);
    }

    [Fact]
    public async Task TargetRemovedDuringCopyOfAShrunkSourceRestoresTheSource()
    {
        var h = ShrinkHarness();
        var removed = false;
        h.Copier.OnChunk = () =>
        {
            if (!removed)
            {
                removed = true;
                h.RemovalMonitor.Remove(h.Plan.Target.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        FailureOf(h).Code.ShouldBe(nameof(PlatformErrorCode.TargetDeviceRemoved));
        h.Compensations().ShouldBe([CompensationKind.DeleteSnapshot, CompensationKind.RestoreSource, CompensationKind.AllowSleep]);
        h.Resizer.Restores.Count.ShouldBe(1);
        report.SourceRestored.ShouldBe(true);
    }

    [Fact]
    public async Task FailedCompensationBecauseTheTargetIsGoneIsReportedAndKeepsTheOriginalCode()
    {
        var h = new ExecuteCloneTests.Harness();
        h.Target.FailOn[nameof(ITargetDisk.DeleteLayoutAsync)] = new PlatformException(PlatformErrorCode.TargetDeviceRemoved, "layout");
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.CommitLayout })
            {
                h.RemovalMonitor.Remove(h.Plan.Target.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        FailureOf(h).Code.ShouldBe(nameof(PlatformErrorCode.TargetDeviceRemoved));
        FailureOf(h).Phase.ShouldBe(ExecutionPhase.CommitLayout);
        var layout = h.Events.OfType<CompensationCompleted>().First(c => c.Kind == CompensationKind.DeleteTargetLayout);
        layout.Success.ShouldBeFalse();
        h.Events.OfType<CompensationCompleted>().Last().Kind.ShouldBe(CompensationKind.AllowSleep);
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task SourceReadErrorAbortsWithTheSourceMayHavePhysicalProblemsCode()
    {
        var h = new ExecuteCloneTests.Harness();
        h.Copier.FailOn[nameof(IVolumeCopier.CopyAsync)] = new PlatformException(PlatformErrorCode.SourceReadError, "read");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        FailureOf(h).Code.ShouldBe(nameof(PlatformErrorCode.SourceReadError));
        h.Compensations().ShouldBe(CopyCompensations);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
    }

    [Fact]
    public async Task RemovalAfterTheStructuralVerificationDoesNotAbortACloneThatIsAlreadyVerified()
    {
        var h = new ExecuteCloneTests.Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.Cleanup })
            {
                h.RemovalMonitor.Remove(h.Plan.Target.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Events.OfType<ExecutionFailed>().ShouldBeEmpty();
        h.Compensations().ShouldBeEmpty();
    }

    [Fact]
    public async Task RemovalOfAnUnrelatedDiskIsIgnored()
    {
        var h = new ExecuteCloneTests.Harness();
        h.Copier.OnChunk = () => h.RemovalMonitor.Remove(h.Plan.Target.Number + 100);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
    }

    [Fact]
    public async Task RemovalDuringACancellationPrevailsAsAFailure()
    {
        var h = new ExecuteCloneTests.Harness();
        var acted = false;
        h.Copier.OnChunk = () =>
        {
            if (!acted)
            {
                acted = true;
                h.Orchestrator.RequestCancel();
                h.RemovalMonitor.Remove(h.Plan.Source.Number);
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        FailureOf(h).Code.ShouldBe(nameof(PlatformErrorCode.SourceDeviceRemoved));
        h.Events.OfType<ExecutionCancelled>().ShouldBeEmpty();
    }

    [Fact]
    public async Task WatchStartsForTheSourceAndTargetAndStopsWhenTheRunEnds()
    {
        var h = new ExecuteCloneTests.Harness();

        await h.RunAsync(Ct);

        h.RemovalMonitor.WatchesStarted.ShouldBe(1);
        h.RemovalMonitor.LastWatchedDisks.ShouldBe([h.Plan.Source.Number, h.Plan.Target.Number]);
        h.RemovalMonitor.ActiveWatches.ShouldBe(0);
    }

    [Fact]
    public async Task WatchStopsWhenTheRunFails()
    {
        var h = new ExecuteCloneTests.Harness();
        h.Copier.FailOn[nameof(IVolumeCopier.CopyAsync)] = new PlatformException(PlatformErrorCode.SourceReadError, "read");

        await h.RunAsync(Ct);

        h.RemovalMonitor.ActiveWatches.ShouldBe(0);
    }

    [Fact]
    public async Task WatchFailureIsLoggedAndTheCloneStillRuns()
    {
        // Without the monitor a removal still surfaces as an I/O error, so a monitor that cannot start must not block cloning.
        var h = new ExecuteCloneTests.Harness();
        h.RemovalMonitor.FailOn[nameof(IDeviceRemovalMonitor.Watch)] = new PlatformException(PlatformErrorCode.Unknown, "cfgmgr");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        var warning = h.Log.Events.Single(e => e.Event == "DeviceMonitorUnavailable");
        warning.Level.ShouldBe(LogLevelKind.Warning);
        ((string?)warning.Properties["TechnicalDetail"]).ShouldNotBeNull().ShouldContain("cfgmgr");
    }

    private static ExecutionFailure FailureOf(ExecuteCloneTests.Harness h) => h.Events.OfType<ExecutionFailed>().Single().Failure;

    private static ExecuteCloneTests.Harness ShrinkHarness() => new(ConfirmedShrinkPlan(), ShrinkInventory());
}
