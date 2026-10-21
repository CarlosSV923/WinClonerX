using Shouldly;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using static WinCopy.Domain.Tests.Execution.ConfirmedPlanFixtures;
using static WinCopy.Domain.Tests.Planning.PlanningFixtures;
using static WinCopy.Domain.Tests.Planning.ShrinkFixtures;

namespace WinCopy.Domain.Tests.Execution;

/// <summary>Solicitud de cancelación: qué fases la interrumpen, cuáles la difieren y cuándo se ignora (FR-024, T095).</summary>
public sealed class CloneExecutionCancelTests
{
    private static readonly ExecutionId Id = new(new Guid("33333333-3333-3333-3333-333333333333"));
    private static readonly DateTimeOffset T0 = CapturedAt;

    // ----- Fases interrumpibles y no interrumpibles -----

    [Theory]
    [InlineData(ExecutionPhase.Revalidate)]
    [InlineData(ExecutionPhase.Snapshot)]
    [InlineData(ExecutionPhase.PrepareTarget)]
    [InlineData(ExecutionPhase.CopyData)]
    [InlineData(ExecutionPhase.ExpandFileSystem)]
    [InlineData(ExecutionPhase.StructuralVerification)]
    public void InterruptiblePhasesTakeTheCancelAtOnce(ExecutionPhase phase)
    {
        var execution = Start(ConfirmedPlanWith(phase));
        BeginDuring(execution, phase);

        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Cancelling);
        execution.CancelPending.ShouldBeFalse();
    }

    /// <summary>
    /// Cleanup y RestoreSource no aparecen aquí: solo se ejecutan después de la verificación estructural, así que una
    /// solicitud en ese punto se ignora (ver las pruebas de "solicitudes que se ignoran").
    /// </summary>
    [Theory]
    [InlineData(ExecutionPhase.ShrinkSource)]
    [InlineData(ExecutionPhase.CommitLayout)]
    [InlineData(ExecutionPhase.RepairBoot)]
    public void NonInterruptiblePhasesDeferTheCancelWhileTheyRun(ExecutionPhase phase)
    {
        var execution = Start(ConfirmedPlanWith(phase));
        BeginDuring(execution, phase);

        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Running);
        execution.CancelPending.ShouldBeTrue();
        execution.CanRequestCancel.ShouldBeFalse();
    }

    [Theory]
    [InlineData(ExecutionPhase.Revalidate, true)]
    [InlineData(ExecutionPhase.Snapshot, true)]
    [InlineData(ExecutionPhase.PrepareTarget, true)]
    [InlineData(ExecutionPhase.CopyData, true)]
    [InlineData(ExecutionPhase.CommitLayout, false)]
    [InlineData(ExecutionPhase.ExpandFileSystem, true)]
    [InlineData(ExecutionPhase.RepairBoot, false)]
    [InlineData(ExecutionPhase.StructuralVerification, true)]
    [InlineData(ExecutionPhase.FullVerification, true)]
    [InlineData(ExecutionPhase.ShrinkSource, false)]
    [InlineData(ExecutionPhase.Cleanup, false)]
    [InlineData(ExecutionPhase.RestoreSource, false)]
    public void IsInterruptibleMatchesTheCancellationRules(ExecutionPhase phase, bool expected)
    {
        CloneExecution.IsInterruptible(phase).ShouldBe(expected);
    }

    // ----- Cancelación diferida -----

    [Fact]
    public void CancelDuringCommitLayoutTakesEffectWhenTheLayoutIsCommitted()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CommitLayout);

        execution.RequestCancel();
        execution.CancelPending.ShouldBeTrue();

        execution.CompletePhase(ExecutionPhase.CommitLayout, T0);

        execution.State.ShouldBe(ExecutionState.Cancelling);
        execution.CancelPending.ShouldBeFalse();
        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void CancelDuringRepairBootTakesEffectBeforeVerificationStarts()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.RepairBoot);

        execution.RequestCancel();
        execution.CompletePhase(ExecutionPhase.RepairBoot, T0);

        execution.State.ShouldBe(ExecutionState.Cancelling);
        Should.Throw<InvalidOperationException>(() => execution.BeginPhase(ExecutionPhase.StructuralVerification, T0));
        execution.RequiredCompensations.ShouldBe(new[]
        {
            CompensationKind.DeleteTargetLayout,
            CompensationKind.DeleteSnapshot,
            CompensationKind.ReleaseMounts,
            CompensationKind.AllowSleep,
        });
    }

    [Fact]
    public void RepeatedRequestWhilePendingHasNoFurtherEffect()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.RepairBoot);

        execution.RequestCancel();
        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Running);
        execution.CancelPending.ShouldBeTrue();
        execution.CompletePhase(ExecutionPhase.RepairBoot, T0);
        execution.State.ShouldBe(ExecutionState.Cancelling);
    }

    [Fact]
    public void RepeatedRequestWhileCancellingHasNoEffect()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CopyData);

        execution.RequestCancel();
        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Cancelling);
    }

    [Fact]
    public void FailureWhileTheCancelIsPendingFailsTheRun()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.RepairBoot);
        execution.RequestCancel();

        execution.Fail("RegistryEditFailed", "no se pudo escribir MountedDevices", T0);

        execution.State.ShouldBe(ExecutionState.Failing);
        execution.CancelPending.ShouldBeFalse();
        execution.Failure!.Phase.ShouldBe(ExecutionPhase.RepairBoot);
    }

    [Fact]
    public void DeferredCancelDuringShrinkSourceStillRestoresTheSource()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.ShrinkSource);
        execution.RequestCancel();

        execution.CompletePhase(ExecutionPhase.ShrinkSource, T0);

        execution.RequiredCompensations.ShouldContain(CompensationKind.RestoreSource);
    }

    [Fact]
    public void ProgressIsStillAcceptedWhileTheCopyIsBeingCancelled()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.CopyData);
        execution.RequestCancel();

        Should.NotThrow(() => execution.ReportProgress(new CloneProgress(10, 100, 1.0, null), T0));
        execution.State.ShouldBe(ExecutionState.Cancelling);
    }

    // ----- Solicitudes que se ignoran -----

    [Fact]
    public void CancelAfterStructuralVerificationPassedIsIgnored()
    {
        var execution = Start();
        BeginDuring(execution, ExecutionPhase.Cleanup);

        execution.CanRequestCancel.ShouldBeFalse();
        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Running);
        execution.CancelPending.ShouldBeFalse();

        execution.CompletePhase(ExecutionPhase.Cleanup, T0);
        execution.Finish(T0.AddMinutes(5), Report(CloneOutcome.Completed));

        execution.State.ShouldBe(ExecutionState.Completed);
    }

    [Fact]
    public void CancelDuringRestoreSourceAfterVerificationIsIgnored()
    {
        var execution = Start(ConfirmedShrinkPlan(ByteSize.FromGiB(45)));
        BeginDuring(execution, ExecutionPhase.RestoreSource);

        execution.RequestCancel();
        execution.CompletePhase(ExecutionPhase.RestoreSource, T0);

        execution.State.ShouldBe(ExecutionState.Running);
        execution.Finish(T0.AddMinutes(5), Report(CloneOutcome.Completed, sourceRestored: true));
        execution.State.ShouldBe(ExecutionState.Completed);
    }

    [Fact]
    public void CancelBeforeTheFirstPhaseMovesToCancellingWithNothingBegun()
    {
        var execution = Start();

        execution.CanRequestCancel.ShouldBeTrue();
        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Cancelling);
        execution.RequiredCompensations.ShouldBe(new[] { CompensationKind.AllowSleep });
    }

    [Fact]
    public void CancelAfterFinishHasNoEffect()
    {
        var execution = Start();
        CompleteEveryPhase(execution);
        execution.Finish(T0.AddMinutes(5), Report(CloneOutcome.Completed));

        execution.RequestCancel();

        execution.State.ShouldBe(ExecutionState.Completed);
        execution.Result!.Outcome.ShouldBe(CloneOutcome.Completed);
    }

    // ----- Ayudantes -----

    private static CloneExecution Start(ClonePlan? plan = null) =>
        CloneExecution.Start(Id, plan ?? Confirmed(), T0);

    /// <summary>Plan típico con la fase indicada planificada. Las fases de reducción solo existen en un plan que reduce el origen.</summary>
    private static ClonePlan ConfirmedPlanWith(ExecutionPhase phase) => phase is ExecutionPhase.ShrinkSource or ExecutionPhase.RestoreSource
        ? ConfirmedShrinkPlan(ByteSize.FromGiB(45))
        : Confirmed(targetSize: phase == ExecutionPhase.ExpandFileSystem ? 512 * Gib : 256 * Gib);

    private static CloneResultReport Report(CloneOutcome outcome, bool? sourceRestored = null) =>
        new(outcome, null, null, sourceRestored, null, TimeSpan.FromMinutes(1), @"C:\ProgramData\WinCopy\logs\run.json", []);

    /// <summary>Completa, en orden, las fases planificadas antes de <paramref name="phase"/> y la inicia sin completarla.</summary>
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

    private static void CompleteEveryPhase(CloneExecution execution)
    {
        foreach (var phase in execution.PlannedPhases)
        {
            execution.BeginPhase(phase, T0);
            execution.CompletePhase(phase, T0);
        }
    }
}
