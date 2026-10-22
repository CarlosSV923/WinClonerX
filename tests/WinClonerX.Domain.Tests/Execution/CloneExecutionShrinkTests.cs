using Shouldly;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using Xunit;
using static WinClonerX.Domain.Tests.Execution.ConfirmedPlanFixtures;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;
using static WinClonerX.Domain.Tests.Planning.ShrinkFixtures;

namespace WinClonerX.Domain.Tests.Execution;

public sealed class CloneExecutionShrinkTests
{
    private static readonly ExecutionId Id = new(new Guid("22222222-2222-2222-2222-222222222222"));
    private static readonly DateTimeOffset T0 = CapturedAt;

    /// <summary>Fases de un plan que reduce el origen al espacio disponible para Windows en el destino.</summary>
    private static readonly ExecutionPhase[] ShrinkPhases =
    [
        ExecutionPhase.Revalidate,
        ExecutionPhase.ShrinkSource,
        ExecutionPhase.Snapshot,
        ExecutionPhase.PrepareTarget,
        ExecutionPhase.CopyData,
        ExecutionPhase.CommitLayout,
        ExecutionPhase.RepairBoot,
        ExecutionPhase.StructuralVerification,
        ExecutionPhase.Cleanup,
        ExecutionPhase.RestoreSource,
    ];

    // ----- Fases planificadas -----

    [Fact]
    public void AShrinkPlanStartsWithShrinkSourceAndEndsWithRestoreSource()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));

        execution.PlannedPhases.ShouldBe(ShrinkPhases);
        execution.CurrentPhase.ShouldBe(ExecutionPhase.Revalidate);
    }

    [Fact]
    public void AShrinkPlanWithoutFileSystemGrowthOmitsExpandFileSystem()
    {
        var execution = Start(ConfirmedShrinkPlan(new ByteSize(SmallTargetWindowsLength)));

        execution.PlannedPhases.ShouldBe(new[]
        {
            ExecutionPhase.Revalidate,
            ExecutionPhase.ShrinkSource,
            ExecutionPhase.Snapshot,
            ExecutionPhase.PrepareTarget,
            ExecutionPhase.CopyData,
            ExecutionPhase.CommitLayout,
            ExecutionPhase.RepairBoot,
            ExecutionPhase.StructuralVerification,
            ExecutionPhase.Cleanup,
            ExecutionPhase.RestoreSource,
        });
    }

    [Fact]
    public void AnUnshrunkPlanNeverPlansRestoreSource()
    {
        var execution = Start(Confirmed());

        execution.PlannedPhases.ShouldNotContain(ExecutionPhase.RestoreSource);
        execution.PlannedPhases.ShouldNotContain(ExecutionPhase.ShrinkSource);
    }

    [Fact]
    public void RestoreSourceCannotStartBeforeItsTurn()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));

        Should.Throw<InvalidOperationException>(() => execution.BeginPhase(ExecutionPhase.RestoreSource, T0));
    }

    // ----- Camino feliz -----

    [Fact]
    public void TheSuccessfulRunRestoresTheSourceLastAndCompletes()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        CompleteEveryPhase(execution);

        execution.Finish(T0.AddMinutes(10), Report(CloneOutcome.Completed, sourceRestored: true));

        execution.State.ShouldBe(ExecutionState.Completed);
        execution.Result!.SourceRestored.ShouldBe(true);
        execution.RestoreFailure.ShouldBeNull();
        execution.Events[^1].ShouldBeOfType<ExecutionCompleted>();
    }

    // ----- Compensaciones -----

    [Fact]
    public void AFailureAfterShrinkSourceRequiresRestoreSourceBeforeAllowSleep()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        FailDuring(execution, ExecutionPhase.CopyData);

        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteSnapshot,
            CompensationKind.RestoreSource,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void AFailureDuringShrinkSourceRequiresOnlyRestoreSourceAndAllowSleep()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        FailDuring(execution, ExecutionPhase.ShrinkSource);

        execution.RequiredCompensations.ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
    }

    [Fact]
    public void AFailureInCommitLayoutOrdersDeleteTargetLayoutBeforeRestoreSource()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        FailDuring(execution, ExecutionPhase.CommitLayout);

        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.RestoreSource,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void AFailureInRepairBootOrdersReleaseMountsBeforeRestoreSource()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        FailDuring(execution, ExecutionPhase.RepairBoot);

        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.ReleaseMounts,
            CompensationKind.RestoreSource,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void AFailureDuringRestoreSourceItselfRequestsAnotherRestoreAttempt()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.RestoreSource);

        execution.Fail("SourceRestoreError", "el tamaño no se pudo restaurar", T0);

        execution.RequiredCompensations.ShouldContain(CompensationKind.RestoreSource);
        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.ReleaseMounts,
            CompensationKind.RestoreSource,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void RestoreSourceCompensationMustFollowTheRequiredOrder()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        FailDuring(execution, ExecutionPhase.CopyData);

        Should.Throw<InvalidOperationException>(() => execution.CompleteCompensation(CompensationKind.RestoreSource, true, null, T0));

        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, true, null, T0);
        execution.CompleteCompensation(CompensationKind.RestoreSource, true, null, T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);

        execution.Compensations.Select(record => record.Kind).ShouldBe(new[]
        {
            CompensationKind.DeleteSnapshot,
            CompensationKind.RestoreSource,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void AFailedRestoreCompensationIsRecordedWithoutHidingTheOriginalError()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        FailDuring(execution, ExecutionPhase.CopyData);

        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, true, null, T0);
        execution.CompleteCompensation(CompensationKind.RestoreSource, false, "el disco está bloqueado por otro proceso", T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);
        execution.Finish(T0.AddMinutes(1), Report(CloneOutcome.Failed, sourceRestored: false));

        execution.State.ShouldBe(ExecutionState.Failed);
        execution.Failure!.Code.ShouldBe("CopyReadError");
        execution.Failure!.Phase.ShouldBe(ExecutionPhase.CopyData);
        var restore = execution.Compensations.Single(record => record.Kind == CompensationKind.RestoreSource);
        restore.Success.ShouldBeFalse();
        restore.Detail.ShouldBe("el disco está bloqueado por otro proceso");
        execution.Events.OfType<CompensationCompleted>()
            .Any(e => e.Kind == CompensationKind.RestoreSource && !e.Success)
            .ShouldBeTrue();
    }

    // ----- Cancelación -----

    [Fact]
    public void CancelDuringShrinkSourceIsDeferredUntilTheReductionEndsAndThenRequiresRestoreSource()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.ShrinkSource);

        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Running);
        execution.CancelPending.ShouldBeTrue();

        execution.CompletePhase(ExecutionPhase.ShrinkSource, T0);

        execution.State.ShouldBe(ExecutionState.Cancelling);
        execution.RequiredCompensations.ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
    }

    [Fact]
    public void CancelDuringCopyDataRestoresTheSourceBeforeFinishingAsCancelled()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.CopyData);
        execution.RequestCancel();

        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteSnapshot,
            CompensationKind.RestoreSource,
            CompensationKind.AllowSleep,
        });

        execution.CompleteCompensation(CompensationKind.DeleteSnapshot, true, null, T0);
        execution.CompleteCompensation(CompensationKind.RestoreSource, true, null, T0);
        execution.CompleteCompensation(CompensationKind.AllowSleep, true, null, T0);
        execution.Finish(T0.AddMinutes(2), Report(CloneOutcome.Cancelled, sourceRestored: true));

        execution.State.ShouldBe(ExecutionState.Cancelled);
        execution.Result!.Outcome.ShouldBe(CloneOutcome.Cancelled);
    }

    // ----- Fallo de la restauración en el camino feliz (no es un fallo de la ejecución) -----

    [Fact]
    public void ARestoreFailureOnTheSuccessPathCompletesTheRunWithTheSourceNotRestored()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.RestoreSource);

        execution.RecordRestoreFailure("el origen sigue reducido");
        execution.CompletePhase(ExecutionPhase.RestoreSource, T0);
        execution.Finish(T0.AddMinutes(10), Report(CloneOutcome.Completed, sourceRestored: false));

        execution.State.ShouldBe(ExecutionState.Completed);
        execution.Failure.ShouldBeNull();
        execution.RestoreFailure.ShouldBe("el origen sigue reducido");
        execution.Result!.SourceRestored.ShouldBe(false);
        execution.Result!.Outcome.ShouldBe(CloneOutcome.Completed);
        execution.Compensations.ShouldBeEmpty();
    }

    [Fact]
    public void ARestoreFailureIsNotAnExecutionFailure()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.RestoreSource);

        execution.RecordRestoreFailure("el origen sigue reducido");

        execution.State.ShouldBe(ExecutionState.Running);
        execution.Failure.ShouldBeNull();
    }

    [Fact]
    public void CompletingWithSourceRestoredTrueAfterARestoreFailureIsRejected()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.RestoreSource);
        execution.RecordRestoreFailure("el origen sigue reducido");
        execution.CompletePhase(ExecutionPhase.RestoreSource, T0);

        Should.Throw<ArgumentException>(() => execution.Finish(T0, Report(CloneOutcome.Completed, sourceRestored: true)));
    }

    [Fact]
    public void CompletingAShrinkPlanRequiresTheSourceRestoredFlag()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        CompleteEveryPhase(execution);

        Should.Throw<ArgumentException>(() => execution.Finish(T0, Report(CloneOutcome.Completed, sourceRestored: null)));
    }

    [Fact]
    public void RecordingARestoreFailureIsOnlyValidDuringRestoreSource()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.CopyData);

        Should.Throw<InvalidOperationException>(() => execution.RecordRestoreFailure("detalle"));
    }

    [Fact]
    public void RecordingARestoreFailureIsRejectedWhenTheRunDoesNotShrinkTheSource()
    {
        var execution = Start(Confirmed());
        BeginDuring(execution, ExecutionPhase.Cleanup);

        Should.Throw<InvalidOperationException>(() => execution.RecordRestoreFailure("detalle"));
    }

    // ----- Ayudantes -----

    private static CloneExecution Start(ClonePlan plan) => CloneExecution.Start(Id, plan, T0);

    private static CloneResultReport Report(CloneOutcome outcome, bool? sourceRestored) =>
        new(outcome, null, null, sourceRestored, null, TimeSpan.FromMinutes(1), @"C:\ProgramData\WinClonerX\logs\run.json", []);

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

    private static void FailDuring(CloneExecution execution, ExecutionPhase phase)
    {
        BeginDuring(execution, phase);
        execution.Fail("CopyReadError", "fallo de prueba", T0);
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
