using Shouldly;
using WinCopy.Application.Tests.TestData;
using WinCopy.Domain.Execution;
using Xunit;
using static WinCopy.Application.Tests.TestData.ShrinkTestData;

namespace WinCopy.Application.Tests.UseCases;

/// <summary>
/// Cancelación de una ejecución con <c>RequestCancel</c> y con el token de <c>ExecuteAsync</c>. Interrumpe las fases
/// interrumpibles, difiere las que no lo son hasta que terminan y se ignora después de la verificación estructural (T100).
/// </summary>
public sealed class CancelExecutionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CancelDuringCopyDataEndsCancelledAfterTheSnapshotIsDeleted()
    {
        var h = new ExecuteCloneTests.Harness();
        h.Copier.OnChunk = () => h.Orchestrator.RequestCancel();

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        report.Failure.ShouldBeNull();
        h.Compensations().ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
        h.Snapshot!.Deleted.ShouldBeTrue();
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Events.OfType<ExecutionCancelled>().Count().ShouldBe(1);
        h.Events.OfType<ExecutionFailed>().ShouldBeEmpty();
        var cancelLog = h.Log.Events.Where(e => e.Event == "CancelRequested").ToArray();
        cancelLog.Length.ShouldBe(1);
        cancelLog[0].Properties["Phase"].ShouldBe(nameof(ExecutionPhase.CopyData));
        cancelLog[0].Properties["CancelPending"].ShouldBe(false);
    }

    [Fact]
    public async Task CancelDuringCopyDataOfAShrunkSourceEndsCancelledWithTheSourceRestored()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedShrinkPlan(), ShrinkInventory());
        h.Copier.OnChunk = () => h.Orchestrator.RequestCancel();

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        report.Failure.ShouldBeNull();
        h.Compensations().ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        h.Resizer.Restores.Single().Length.ShouldBe(h.Plan.SourceShrink!.OriginalLength);
        report.SourceRestored.ShouldBe(true);
        h.Events.OfType<ExecutionCancelled>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task CancelDuringShrinkSourceCompletesTheReductionThenRestoresTheSource()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedShrinkPlan(), ShrinkInventory());
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.ShrinkSource })
            {
                h.Orchestrator.RequestCancel();
                h.Orchestrator.RequestCancel();
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        h.Resizer.Shrinks.Count.ShouldBe(1);
        h.Phases().ShouldNotContain(ExecutionPhase.Snapshot);
        h.Compensations().ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        h.Resizer.Restores.Count.ShouldBe(1);
        report.SourceRestored.ShouldBe(true);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        var cancelLog = h.Log.Events.Where(e => e.Event == "CancelRequested").ToArray();
        cancelLog.Length.ShouldBe(1);
        cancelLog[0].Properties["Phase"].ShouldBe(nameof(ExecutionPhase.ShrinkSource));
        cancelLog[0].Properties["CancelPending"].ShouldBe(true);
    }

    [Fact]
    public async Task CancelDuringCommitLayoutCompletesTheLayoutThenDeletesIt()
    {
        var h = new ExecuteCloneTests.Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.CommitLayout })
            {
                h.Orchestrator.RequestCancel();
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        report.Failure.ShouldBeNull();
        h.Target.Calls.ShouldContain("CommitLayoutAsync");
        h.Target.Calls.ShouldContain("DeleteLayoutAsync");
        h.Phases().ShouldNotContain(ExecutionPhase.ExpandFileSystem);
        h.Phases().ShouldNotContain(ExecutionPhase.RepairBoot);
        h.Compensations().ShouldBe(new[] { CompensationKind.DeleteTargetLayout, CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
    }

    [Fact]
    public async Task CancelDuringRepairBootCompletesTheBootRepairThenReleasesTheMounts()
    {
        var h = new ExecuteCloneTests.Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.RepairBoot })
            {
                h.Orchestrator.RequestCancel();
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        h.Boot.Rebuilds.ShouldNotBeEmpty();
        h.Phases().ShouldNotContain(ExecutionPhase.StructuralVerification);
        h.Verifier.Calls.ShouldNotContain("VerifyAsync");
        h.Compensations().ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.ReleaseMounts,
            CompensationKind.AllowSleep,
        });
        h.Events.OfType<CompensationCompleted>().ShouldAllBe(compensation => compensation.Success);
    }

    [Fact]
    public async Task CancellationTokenDuringRepairBootTakesEffectAtTheNextBoundary()
    {
        using var cts = new CancellationTokenSource();
        var h = new ExecuteCloneTests.Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.RepairBoot })
            {
                cts.Cancel();
            }
        };

        var report = await h.RunAsync(cts.Token);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        h.Verifier.Calls.ShouldNotContain("VerifyAsync");
        h.Compensations().Last().ShouldBe(CompensationKind.AllowSleep);
        h.Events.OfType<ExecutionCancelled>().Count().ShouldBe(1);
    }

    [Fact]
    public async Task CancelAfterTheStructuralVerificationPassedIsIgnoredAndTheRunCompletes()
    {
        var h = new ExecuteCloneTests.Harness();
        h.OnEvent = e =>
        {
            if (e is PhaseCompleted { Phase: ExecutionPhase.StructuralVerification })
            {
                h.Orchestrator.RequestCancel();
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Phases().ShouldContain(ExecutionPhase.Cleanup);
        h.Compensations().ShouldBeEmpty();
        h.Events.OfType<ExecutionCompleted>().Count().ShouldBe(1);
        h.Events.OfType<ExecutionCancelled>().ShouldBeEmpty();
        h.Log.Events.ShouldNotContain(e => e.Event == "CancelRequested");
    }

    [Fact]
    public async Task CancelDuringTheSourceRestoreIsIgnoredAndTheRunCompletes()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedShrinkPlan(), ShrinkInventory());
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.RestoreSource })
            {
                h.Orchestrator.RequestCancel();
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.SourceRestored.ShouldBe(true);
        h.Resizer.Restores.Count.ShouldBe(1);
        h.Compensations().ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelBeforeTheRunStartsHasNoEffect()
    {
        var h = new ExecuteCloneTests.Harness();

        h.Orchestrator.RequestCancel();
        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Events.OfType<ExecutionCancelled>().ShouldBeEmpty();
    }

    [Fact]
    public async Task CancelAfterTheRunFinishedHasNoEffect()
    {
        var h = new ExecuteCloneTests.Harness();
        var report = await h.RunAsync(Ct);

        h.Orchestrator.RequestCancel();

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Events.OfType<ExecutionCancelled>().ShouldBeEmpty();
        h.Events.OfType<ExecutionCompleted>().Count().ShouldBe(1);
    }
}
