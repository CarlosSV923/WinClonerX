using Shouldly;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Execution.ConfirmedPlanFixtures;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Execution;

public sealed class CloneExecutionTests
{
    private static readonly ExecutionId Id = new(new Guid("11111111-1111-1111-1111-111111111111"));
    private static readonly DateTimeOffset T0 = CapturedAt;

    /// <summary>Fases del plan típico: destino del mismo tamaño y verificación estructural.</summary>
    private static readonly ExecutionPhase[] TypicalPhases =
    [
        ExecutionPhase.Revalidate,
        ExecutionPhase.Snapshot,
        ExecutionPhase.PrepareTarget,
        ExecutionPhase.CopyData,
        ExecutionPhase.CommitLayout,
        ExecutionPhase.RepairBoot,
        ExecutionPhase.StructuralVerification,
        ExecutionPhase.Cleanup,
    ];

    // ----- Inicio y fases planificadas -----

    [Fact]
    public void StartRunsTheTypicalPhasesAndRaisesExecutionStarted()
    {
        var execution = Start();

        execution.State.ShouldBe(ExecutionState.Running);
        execution.CurrentPhase.ShouldBe(ExecutionPhase.Revalidate);
        execution.PlannedPhases.ShouldBe(TypicalPhases);
        execution.Events.Count.ShouldBe(1);
        execution.Events[0].ShouldBeOfType<ExecutionStarted>();
    }

    [Fact]
    public void PlannedPhasesNeverIncludeTheUs2SourcePhases()
    {
        var execution = Start();

        execution.PlannedPhases.ShouldNotContain(ExecutionPhase.ShrinkSource);
        execution.PlannedPhases.ShouldNotContain(ExecutionPhase.RestoreSource);
        execution.PlannedPhases.ShouldNotContain(ExecutionPhase.Done);
    }

    [Fact]
    public void ExpandFileSystemIsPlannedOnlyWhenWindowsGrows()
    {
        var grows = Start(Confirmed(targetSize: 512 * Gib));
        var sameSize = Start();

        grows.PlannedPhases.ShouldContain(ExecutionPhase.ExpandFileSystem);
        grows.PlannedPhases.ShouldBe(new[]
        {
            ExecutionPhase.Revalidate,
            ExecutionPhase.Snapshot,
            ExecutionPhase.PrepareTarget,
            ExecutionPhase.CopyData,
            ExecutionPhase.CommitLayout,
            ExecutionPhase.ExpandFileSystem,
            ExecutionPhase.RepairBoot,
            ExecutionPhase.StructuralVerification,
            ExecutionPhase.Cleanup,
        });
        sameSize.PlannedPhases.ShouldNotContain(ExecutionPhase.ExpandFileSystem);
    }

    [Fact]
    public void FullVerificationIsPlannedOnlyWhenRequested()
    {
        var full = Start(Confirmed(mode: VerificationMode.StructuralAndFull));
        var structural = Start();

        full.PlannedPhases.ShouldBe(new[]
        {
            ExecutionPhase.Revalidate,
            ExecutionPhase.Snapshot,
            ExecutionPhase.PrepareTarget,
            ExecutionPhase.CopyData,
            ExecutionPhase.FullVerification,
            ExecutionPhase.CommitLayout,
            ExecutionPhase.RepairBoot,
            ExecutionPhase.StructuralVerification,
            ExecutionPhase.Cleanup,
        });
        structural.PlannedPhases.ShouldNotContain(ExecutionPhase.FullVerification);
    }

    [Fact]
    public void FullVerificationComesAfterCopyDataAndBeforeTheLayoutAndExpansionWhenBothApply()
    {
        var execution = Start(Confirmed(targetSize: 512 * Gib, mode: VerificationMode.StructuralAndFull));

        var phases = execution.PlannedPhases.ToList();
        phases.IndexOf(ExecutionPhase.FullVerification).ShouldBe(phases.IndexOf(ExecutionPhase.CopyData) + 1);
        phases.IndexOf(ExecutionPhase.FullVerification).ShouldBeLessThan(phases.IndexOf(ExecutionPhase.CommitLayout));
        phases.IndexOf(ExecutionPhase.FullVerification).ShouldBeLessThan(phases.IndexOf(ExecutionPhase.ExpandFileSystem));
    }

    [Fact]
    public void FullVerificationIsInterruptibleSoACancellationDuringItTakesEffectAtOnce()
    {
        var execution = Start(Confirmed(mode: VerificationMode.StructuralAndFull));
        execution.BeginPhase(ExecutionPhase.Revalidate, T0);
        execution.CompletePhase(ExecutionPhase.Revalidate, T0);
        execution.BeginPhase(ExecutionPhase.Snapshot, T0);
        execution.CompletePhase(ExecutionPhase.Snapshot, T0);
        execution.BeginPhase(ExecutionPhase.PrepareTarget, T0);
        execution.CompletePhase(ExecutionPhase.PrepareTarget, T0);
        execution.BeginPhase(ExecutionPhase.CopyData, T0);
        execution.CompletePhase(ExecutionPhase.CopyData, T0);
        execution.BeginPhase(ExecutionPhase.FullVerification, T0);

        CloneExecution.IsInterruptible(ExecutionPhase.FullVerification).ShouldBeTrue();
        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Cancelling);
    }

    [Fact]
    public void StartRejectsADraftPlan()
    {
        var draft = PlanFor(InventoryOf(SystemDisk(), TargetDisk())).Value;

        Should.Throw<InvalidOperationException>(() => CloneExecution.Start(Id, draft, T0));
    }

    [Fact]
    public void StartRejectsAnInvalidatedPlan()
    {
        var plan = Confirmed();
        plan.Invalidate("el disco cambió");

        Should.Throw<InvalidOperationException>(() => CloneExecution.Start(Id, plan, T0));
    }

    // ----- Transiciones de fase -----

    [Fact]
    public void HappyPathCompletesEveryPhaseInOrder()
    {
        var execution = Start();

        foreach (var phase in execution.PlannedPhases)
        {
            execution.BeginPhase(phase, T0);
            if (phase == ExecutionPhase.CopyData)
            {
                execution.ReportProgress(new CloneProgress(50, 100, 1000, null), T0);
            }

            execution.CompletePhase(phase, T0.AddSeconds(1));
        }

        execution.Finish(T0.AddMinutes(10), Report(CloneOutcome.Completed));

        execution.State.ShouldBe(ExecutionState.Completed);
        execution.CurrentPhase.ShouldBe(ExecutionPhase.Done);
        execution.Result.ShouldNotBeNull();
        execution.Result!.Outcome.ShouldBe(CloneOutcome.Completed);
        execution.Failure.ShouldBeNull();
        execution.Compensations.ShouldBeEmpty();
    }

    [Fact]
    public void BeginPhaseRejectsAPhaseOutOfOrder()
    {
        var execution = Start();

        Should.Throw<InvalidOperationException>(() => execution.BeginPhase(ExecutionPhase.CopyData, T0));
    }

    [Fact]
    public void BeginPhaseRejectsANewPhaseWhilePreviousOneIsInProgress()
    {
        var execution = Start();
        execution.BeginPhase(ExecutionPhase.Revalidate, T0);

        Should.Throw<InvalidOperationException>(() => execution.BeginPhase(ExecutionPhase.Snapshot, T0));
    }

    [Fact]
    public void BeginPhaseRejectsAPhaseTwice()
    {
        var execution = Start();
        execution.BeginPhase(ExecutionPhase.Revalidate, T0);
        execution.CompletePhase(ExecutionPhase.Revalidate, T0);

        Should.Throw<InvalidOperationException>(() => execution.BeginPhase(ExecutionPhase.Revalidate, T0));
    }

    [Fact]
    public void CompletePhaseRejectsAPhaseThatIsNotInProgress()
    {
        var execution = Start();

        Should.Throw<InvalidOperationException>(() => execution.CompletePhase(ExecutionPhase.Revalidate, T0));
    }

    [Fact]
    public void CompletePhaseRaisesPhaseCompletedWithTheDuration()
    {
        var execution = Start();
        execution.BeginPhase(ExecutionPhase.Revalidate, T0);

        execution.CompletePhase(ExecutionPhase.Revalidate, T0.AddSeconds(5));

        var completed = execution.Events.OfType<PhaseCompleted>().Single();
        completed.Phase.ShouldBe(ExecutionPhase.Revalidate);
        completed.Duration.ShouldBe(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void BeginPhaseIsRejectedAfterAFailure()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.Revalidate);

        Should.Throw<InvalidOperationException>(() => execution.BeginPhase(ExecutionPhase.Snapshot, T0));
    }

    // ----- Progreso -----

    [Fact]
    public void ProgressIsRejectedOutsideCopyDataAndFullVerification()
    {
        var execution = Start();
        execution.BeginPhase(ExecutionPhase.Revalidate, T0);

        Should.Throw<InvalidOperationException>(() => execution.ReportProgress(new CloneProgress(10, 100, 1, null), T0));
    }

    [Fact]
    public void ProgressInCopyDataUpdatesTheProgressAndRaisesAnEvent()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CopyData);
        var progress = new CloneProgress(25, 100, 500, TimeSpan.FromSeconds(30));

        execution.ReportProgress(progress, T0.AddSeconds(3));

        execution.Progress.ShouldBe(progress);
        var reported = execution.Events.OfType<ProgressReported>().Single();
        reported.Progress.ShouldBe(progress);
        reported.At.ShouldBe(T0.AddSeconds(3));
    }

    [Fact]
    public void ProgressIsAcceptedDuringFullVerification()
    {
        var execution = Start(Confirmed(mode: VerificationMode.StructuralAndFull));
        BeginDuring(execution, ExecutionPhase.FullVerification);

        Should.NotThrow(() => execution.ReportProgress(new CloneProgress(10, 100, 1, null), T0));
    }

    [Theory]
    [InlineData(0L, 200L, 0.0)]
    [InlineData(50L, 200L, 25.0)]
    [InlineData(200L, 200L, 100.0)]
    [InlineData(0L, 0L, 100.0)]
    public void PercentIsTheShareOfBytesDone(long done, long total, double expected)
    {
        new CloneProgress(done, total, 0, null).Percent.ShouldBe(expected);
    }

    [Fact]
    public void ProgressRejectsMoreBytesDoneThanTotal()
    {
        Should.Throw<ArgumentOutOfRangeException>(() =>
        {
            _ = new CloneProgress(201, 200, 0, null);
        });
    }

    // ----- Fallos y compensaciones -----

    [Fact]
    public void FailMovesToFailingAndRecordsTheCurrentPhase()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CopyData);

        execution.Fail("CopyReadError", "lectura incompleta", T0.AddSeconds(7));

        execution.State.ShouldBe(ExecutionState.Failing);
        execution.Failure.ShouldBe(new ExecutionFailure("CopyReadError", ExecutionPhase.CopyData, "lectura incompleta"));
        execution.FailedAt.ShouldBe(T0.AddSeconds(7));
    }

    [Fact]
    public void FailureInRevalidateRequiresOnlyAllowSleep()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.Revalidate);

        execution.RequiredCompensations.ShouldBe(new[] { CompensationKind.AllowSleep });
    }

    [Fact]
    public void FailureInSnapshotRequiresDeleteSnapshot()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.Snapshot);

        execution.RequiredCompensations.ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
    }

    [Fact]
    public void FailureInCopyDataHasNoDeleteTargetLayout()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.CopyData);

        execution.RequiredCompensations.ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
    }

    [Fact]
    public void FailureInCommitLayoutIncludesDeleteTargetLayoutFirst()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.CommitLayout);

        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void FailureAfterCommitLayoutIncludesDeleteTargetLayoutFirst()
    {
        var execution = Start(Confirmed(targetSize: 512 * Gib));
        FailDuring(execution, ExecutionPhase.ExpandFileSystem);

        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void FailureInRepairBootIncludesReleaseMounts()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.RepairBoot);

        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.ReleaseMounts,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void RequiredCompensationsAreUnavailableOutsideFailingAndCancelling()
    {
        var execution = Start();

        Should.Throw<InvalidOperationException>(() =>
        {
            _ = execution.RequiredCompensations;
        });
    }

    [Fact]
    public void CompensationsMustBeCompletedInTheRequiredOrder()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.CopyData);

        Should.Throw<InvalidOperationException>(() => execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0));

        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, true, null, T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);

        execution.Compensations.Select(record => record.Kind).ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
    }

    [Fact]
    public void FailedCompensationIsRecordedWithoutHidingTheOriginalError()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.CopyData);

        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, false, "volumen ocupado", T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);

        execution.Compensations[0].Success.ShouldBeFalse();
        execution.Compensations[0].Detail.ShouldBe("volumen ocupado");
        execution.Failure!.Code.ShouldBe("CopyReadError");
        var failed = execution.Events.OfType<CompensationCompleted>().First();
        failed.Kind.ShouldBe(CompensationKind.DeleteSnapshot);
        failed.Success.ShouldBeFalse();
    }

    [Fact]
    public void FinishAfterFailureRequiresEveryCompensation()
    {
        var execution = Start();
        FailDuring(execution, ExecutionPhase.CopyData);

        Should.Throw<InvalidOperationException>(() => execution.Finish(T0, Report(CloneOutcome.Failed)));

        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, true, null, T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);
        execution.Finish(T0.AddMinutes(1), Report(CloneOutcome.Failed));

        execution.State.ShouldBe(ExecutionState.Failed);
        execution.Result!.Outcome.ShouldBe(CloneOutcome.Failed);
        var last = execution.Events[^1].ShouldBeOfType<ExecutionFailed>();
        last.Failure.ShouldBe(execution.Failure);
    }

    [Fact]
    public void FailureWhileCancellingTakesPrecedenceAndEndsAsFailed()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CopyData);
        execution.RequestCancel();

        execution.Fail("WriteError", "escritura rechazada", T0);

        execution.State.ShouldBe(ExecutionState.Failing);
        execution.Failure!.Phase.ShouldBe(ExecutionPhase.CopyData);
    }

    // ----- Cancelación -----

    [Fact]
    public void CancelDuringCopyDataMovesToCancelling()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CopyData);

        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Cancelling);
        execution.RequiredCompensations.ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep });
    }

    [Fact]
    public void CancelDuringCommitLayoutIsDeferredUntilTheLayoutIsCommitted()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CommitLayout);

        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Running);

        execution.CompletePhase(ExecutionPhase.CommitLayout, T0);

        execution.State.ShouldBe(ExecutionState.Cancelling);
        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void CancelAfterCommitLayoutCompletesMovesToCancellingAtOnce()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CommitLayout);
        execution.CompletePhase(ExecutionPhase.CommitLayout, T0);

        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Cancelling);
    }

    [Fact]
    public void CancelledExecutionFinishesAsCancelled()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CopyData);
        execution.RequestCancel();
        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, true, null, T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);

        execution.Finish(T0.AddMinutes(2), Report(CloneOutcome.Cancelled));

        execution.State.ShouldBe(ExecutionState.Cancelled);
        execution.Result!.Outcome.ShouldBe(CloneOutcome.Cancelled);
        execution.Events[^1].ShouldBeOfType<ExecutionCancelled>();
    }

    // ----- Finalización -----

    [Fact]
    public void FinishRejectsCompletionBeforeEveryPhaseIsDone()
    {
        var execution = Start();
        execution.BeginPhase(ExecutionPhase.Revalidate, T0);
        execution.CompletePhase(ExecutionPhase.Revalidate, T0);

        Should.Throw<InvalidOperationException>(() => execution.Finish(T0, Report(CloneOutcome.Completed)));
    }

    [Fact]
    public void FinishRejectsAReportWhoseOutcomeDoesNotMatchTheState()
    {
        var execution = Start();
        CompleteEveryPhase(execution);

        Should.Throw<ArgumentException>(() => execution.Finish(T0, Report(CloneOutcome.Failed)));
    }

    [Fact]
    public void FinishCannotBeCalledTwice()
    {
        var execution = Start();
        CompleteEveryPhase(execution);
        execution.Finish(T0, Report(CloneOutcome.Completed));

        Should.Throw<InvalidOperationException>(() => execution.Finish(T0, Report(CloneOutcome.Completed)));
    }

    [Fact]
    public void EventsAreEmittedInOrderAndCanBeCleared()
    {
        var execution = Start();
        execution.BeginPhase(ExecutionPhase.Revalidate, T0);
        execution.CompletePhase(ExecutionPhase.Revalidate, T0.AddSeconds(2));
        execution.BeginPhase(ExecutionPhase.Snapshot, T0);
        execution.Fail("SnapshotError", "instantánea no disponible", T0);
        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, true, null, T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);
        execution.Finish(T0, Report(CloneOutcome.Failed));

        execution.Events.Select(e => e.GetType()).ToArray().ShouldBe(new[]
        {
            typeof(ExecutionStarted),
            typeof(PhaseStarted),
            typeof(PhaseCompleted),
            typeof(PhaseStarted),
            typeof(CompensationCompleted),
            typeof(CompensationCompleted),
            typeof(ExecutionFailed),
        });
        execution.Events.OfType<PhaseCompleted>().Single().Duration.ShouldBe(TimeSpan.FromSeconds(2));

        execution.ClearEvents();

        execution.Events.ShouldBeEmpty();
    }

    // ----- Invariante sobre el disco de origen -----

    [Fact]
    public void OnlyShrinkSourceAndRestoreSourceMayModifyTheSource()
    {
        CloneExecution.PhaseMayModifySource(ExecutionPhase.ShrinkSource).ShouldBeTrue();
        CloneExecution.PhaseMayModifySource(ExecutionPhase.RestoreSource).ShouldBeTrue();

        foreach (var phase in Enum.GetValues<ExecutionPhase>()
                     .Where(phase => phase is not (ExecutionPhase.ShrinkSource or ExecutionPhase.RestoreSource)))
        {
            CloneExecution.PhaseMayModifySource(phase).ShouldBeFalse(phase.ToString());
        }
    }

    // ----- Ayudantes -----

    private static CloneExecution Start(ClonePlan? plan = null) =>
        CloneExecution.Start(Id, plan ?? Confirmed(), T0);

    private static CloneResultReport Report(CloneOutcome outcome) =>
        new(outcome, null, null, null, null, TimeSpan.FromMinutes(1), @"C:\ProgramData\WinClonerX\logs\run.json", []);

    /// <summary>Completa, en orden, todas las fases planificadas antes de <paramref name="phase"/> y la inicia sin completarla.</summary>
    private static void BeginDuring(CloneExecution execution, ExecutionPhase phase)
    {
        foreach (var planned in execution.PlannedPhases)
        {
            execution.BeginPhase(planned, T0);
            if (planned == phase)
            {
                return;
            }

            execution.CompletePhase(planned, T0);
        }

        throw new InvalidOperationException($"La fase {phase} no está planificada.");
    }

    /// <summary>Lleva la ejecución a <paramref name="phase"/> en curso y provoca un fallo en ella.</summary>
    private static void FailDuring(CloneExecution execution, ExecutionPhase phase)
    {
        BeginDuring(execution, phase);
        execution.Fail(phase == ExecutionPhase.Revalidate ? "Revalidation" : "CopyReadError", "fallo de prueba", T0);
    }

    private static void CompleteEveryPhase(CloneExecution execution)
    {
        foreach (var phase in execution.PlannedPhases)
        {
            execution.BeginPhase(phase, T0);
            execution.CompletePhase(phase, T0);
        }
    }
}
