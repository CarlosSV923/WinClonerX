using System.Globalization;
using Shouldly;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Application.Tests.TestData;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using FaultPoint = WinCopy.Application.Tests.UseCases.ExecuteCloneTests.FaultPoint;
using static WinCopy.Application.Tests.TestData.PlanningTestData;
using static WinCopy.Application.Tests.TestData.ShrinkTestData;

namespace WinCopy.Application.Tests.UseCases;

/// <summary>
/// Orquestación de un plan con reducción del origen (US2, FR-021): la reducción antes de la instantánea, la restauración
/// al final y las compensaciones. Usa el mismo arnés que <see cref="ExecuteCloneTests"/>.
/// </summary>
public sealed class ExecuteCloneShrinkTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly ExecutionPhase[] ShrinkPhases =
    [
        ExecutionPhase.Revalidate,
        ExecutionPhase.ShrinkSource,
        ExecutionPhase.Snapshot,
        ExecutionPhase.PrepareTarget,
        ExecutionPhase.CopyData,
        ExecutionPhase.CommitLayout,
        ExecutionPhase.ExpandFileSystem,
        ExecutionPhase.RepairBoot,
        ExecutionPhase.StructuralVerification,
        ExecutionPhase.Cleanup,
        ExecutionPhase.RestoreSource,
    ];

    /// <summary>Resultado de cada compensación cuando solo falla la restauración del origen.</summary>
    private static readonly bool[] CompensationOutcomes = [true, false, true];

    [Fact]
    public async Task ShrinkRunsEveryPhaseInOrderWithTheShrinkBeforeTheSnapshotAndTheRestoreLast()
    {
        var shrinksAtSnapshot = -1;
        var restoresAtRestore = -1;
        var snapshotDeletedAtRestore = false;
        var h = NewHarness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.Snapshot })
            {
                shrinksAtSnapshot = h.Resizer.Shrinks.Count;
            }
            else if (e is PhaseStarted { Phase: ExecutionPhase.RestoreSource })
            {
                restoresAtRestore = h.Resizer.Restores.Count;
                snapshotDeletedAtRestore = h.Snapshot?.Deleted ?? false;
            }
        };

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        h.Phases().ShouldBe(ShrinkPhases);
        shrinksAtSnapshot.ShouldBe(1);
        restoresAtRestore.ShouldBe(0);
        snapshotDeletedAtRestore.ShouldBeTrue();
        h.Resizer.Restores.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SuccessRestoresTheSourceOnceWithItsOriginalLengthAndReportsSourceRestored()
    {
        var h = NewHarness();

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.SourceRestored.ShouldBe(true);
        report.NextSteps.ShouldBe(new[] { NextStepCode.ShutDownAndReplaceDisk, NextStepCode.SelectBootDeviceInFirmware });
        var restore = h.Resizer.Restores.Single();
        restore.Partition.ShouldBe(h.Plan.SourceShrink!.Partition);
        restore.Length.ShouldBe(h.Plan.SourceShrink!.OriginalLength);
        h.Compensations().ShouldBeEmpty();
        h.Target.CommittedLayouts.ShouldHaveSingleItem();
        h.Target.Calls.ShouldNotContain("DeleteLayoutAsync");
        h.Events.OfType<ExecutionCompleted>().Count().ShouldBe(1);
        h.Log.Events.Any(e => e.Event == "SourceRestoreFailed").ShouldBeFalse();
    }

    [Fact]
    public async Task MinimumGrownBeyondTheShrunkLengthFailsBeforeShrinkingOrTouchingTheTarget()
    {
        var h = NewHarness();
        h.Resizer.MinimumSize = h.Plan.SourceShrink!.ShrunkLength + ByteSize.FromGiB(1);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        var failure = h.Events.OfType<ExecutionFailed>().Single().Failure;
        failure.Code.ShouldBe("ShrinkInsufficient");
        failure.Phase.ShouldBe(ExecutionPhase.ShrinkSource);
        h.Resizer.Shrinks.ShouldBeEmpty();
        h.Resizer.Restores.ShouldBeEmpty();
        h.Resizer.Calls.ShouldNotContain("ShrinkAsync");
        h.Resizer.Calls.ShouldNotContain("RestoreAsync");
        h.Snapshots.Calls.ShouldBeEmpty();
        h.Targets.OpenedPlans.ShouldBeEmpty();
        h.Power.IsSleepBlocked.ShouldBeFalse();
        // El dominio exige el registro RestoreSource porque ShrinkSource empezó; no llama a RestoreAsync porque no hubo reducción.
        h.Compensations().ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        h.Events.OfType<CompensationCompleted>().ShouldAllBe(compensation => compensation.Success);
    }

    [Fact]
    public async Task MinimumQueryFailureIsShrinkFailedAndNothingIsShrunk()
    {
        var h = NewHarness();
        h.Resizer.FailOn[nameof(ISourceResizer.GetMinimumSizeAsync)] = new InvalidOperationException("vds unavailable");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        var failure = h.Events.OfType<ExecutionFailed>().Single().Failure;
        failure.Code.ShouldBe("ShrinkFailed");
        failure.Phase.ShouldBe(ExecutionPhase.ShrinkSource);
        h.Resizer.Shrinks.ShouldBeEmpty();
        h.Resizer.Restores.ShouldBeEmpty();
        h.Targets.OpenedPlans.ShouldBeEmpty();
        h.Compensations().ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
    }

    [Fact]
    public async Task ShrinkFailureRestoresTheSourceAndNeverOpensTheTarget()
    {
        var h = NewHarness();
        h.Resizer.FailOn[nameof(ISourceResizer.ShrinkAsync)] = new InvalidOperationException("vds busy");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        var failure = h.Events.OfType<ExecutionFailed>().Single().Failure;
        failure.Code.ShouldBe("ShrinkFailed");
        failure.Phase.ShouldBe(ExecutionPhase.ShrinkSource);
        var restore = h.Resizer.Restores.Single();
        restore.Partition.ShouldBe(h.Plan.SourceShrink!.Partition);
        restore.Length.ShouldBe(h.Plan.SourceShrink!.OriginalLength);
        h.Targets.OpenedPlans.ShouldBeEmpty();
        h.Snapshots.Calls.ShouldBeEmpty();
        h.Compensations().ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        h.Events.OfType<CompensationCompleted>().ShouldAllBe(compensation => compensation.Success);
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Theory]
    [InlineData(FaultPoint.Snapshot)]
    [InlineData(FaultPoint.PrepareTarget)]
    [InlineData(FaultPoint.CopyData)]
    [InlineData(FaultPoint.CommitLayout)]
    [InlineData(FaultPoint.RepairBoot)]
    [InlineData(FaultPoint.StructuralVerification)]
    public async Task FailureAfterTheShrinkCompensatesInOrderAndRestoresTheSource(FaultPoint fault)
    {
        var h = NewHarness();
        h.Inject(fault);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Compensations().ShouldBe(ExpectedCompensations(fault));
        h.Events.OfType<CompensationCompleted>().ShouldAllBe(compensation => compensation.Success);
        h.Target.Calls.Contains("DeleteLayoutAsync").ShouldBe(ExpectedCompensations(fault).Contains(CompensationKind.DeleteTargetLayout));
        var restore = h.Resizer.Restores.Single();
        restore.Partition.ShouldBe(h.Plan.SourceShrink!.Partition);
        restore.Length.ShouldBe(h.Plan.SourceShrink!.OriginalLength);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Power.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task CancellationDuringCopyDataRestoresTheSourceAndEndsCancelled()
    {
        using var cts = new CancellationTokenSource();
        var h = NewHarness();
        h.Copier.OnChunk = () => cts.Cancel();

        var report = await h.RunAsync(cts.Token);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        h.Compensations().ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        var restore = h.Resizer.Restores.Single();
        restore.Length.ShouldBe(h.Plan.SourceShrink!.OriginalLength);
        report.SourceRestored.ShouldBe(true);
        h.Events.OfType<ExecutionCancelled>().Count().ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task CancellationTokenDuringShrinkSourceCompletesTheReductionThenRestoresIt()
    {
        // ShrinkSource no es interrumpible: la cancelación pedida con el token se atiende al terminar la reducción.
        using var cts = new CancellationTokenSource();
        var h = NewHarness();
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.ShrinkSource })
            {
                cts.Cancel();
            }
        };

        var report = await h.RunAsync(cts.Token);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        h.Resizer.Shrinks.Count.ShouldBe(1);
        h.Resizer.Restores.Count.ShouldBe(1);
        h.Compensations().ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        report.SourceRestored.ShouldBe(true);
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task RestoreFailureOnSuccessCompletesWithSourceNotRestoredKeepsTheLayoutAndWarns()
    {
        var h = NewHarness();
        h.Resizer.FailOn[nameof(ISourceResizer.RestoreAsync)] = new InvalidOperationException("vds restore busy");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.SourceRestored.ShouldBe(false);
        report.NextSteps.ShouldBe(new[] { NextStepCode.ShutDownAndReplaceDisk, NextStepCode.SelectBootDeviceInFirmware });
        h.Phases().Last().ShouldBe(ExecutionPhase.RestoreSource);
        h.Target.CommittedLayouts.ShouldHaveSingleItem();
        h.Target.Calls.ShouldNotContain("DeleteLayoutAsync");
        h.Compensations().ShouldBeEmpty();
        h.Events.OfType<ExecutionFailed>().ShouldBeEmpty();
        h.Events.OfType<ExecutionCompleted>().Count().ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();

        var warning = h.Log.Events.Single(e => e.Event == "SourceRestoreFailed");
        warning.Level.ShouldBe(LogLevelKind.Warning);
        warning.Properties["Partition"].ShouldBe(h.Plan.SourceShrink!.Partition.Value.ToString("D", CultureInfo.InvariantCulture));
        warning.Properties["OriginalLength"].ShouldBe(h.Plan.SourceShrink!.OriginalLength.Bytes);
        warning.Properties.ContainsKey("TechnicalDetail").ShouldBeTrue();
        warning.Properties.Keys.Intersect(LogEvent.ReservedPropertyNames).ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreSourceCompensationFailureIsRecordedAndTheOriginalErrorStays()
    {
        var h = NewHarness();
        h.Inject(FaultPoint.CopyData);
        h.Resizer.FailOn[nameof(ISourceResizer.RestoreAsync)] = new InvalidOperationException("vds restore busy");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        var failure = h.Events.OfType<ExecutionFailed>().Single().Failure;
        failure.Code.ShouldBe("SourceReadError");
        failure.Phase.ShouldBe(ExecutionPhase.CopyData);
        h.Compensations().ShouldBe(new[] { CompensationKind.DeleteSnapshot, CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        h.Events.OfType<CompensationCompleted>().Select(compensation => compensation.Success).ToArray().ShouldBe(CompensationOutcomes);
        report.SourceRestored.ShouldBe(false);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Power.Releases.ShouldBe(1);
    }

    [Fact]
    public async Task RevalidationFailureBeforeTheShrinkTouchesNothingOfTheSource()
    {
        var h = NewHarness();
        h.Inject(FaultPoint.Revalidate);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Plan.Status.ShouldBe(PlanStatus.Invalidated);
        h.Phases().ShouldBe(new[] { ExecutionPhase.Revalidate });
        h.Resizer.Calls.ShouldBeEmpty();
        h.Compensations().ShouldBe(new[] { CompensationKind.AllowSleep });
    }

    [Fact]
    public async Task TheResizerIsOnlyEverCalledWithTheShrinkPartition()
    {
        var happy = NewHarness();
        await happy.RunAsync(Ct);

        var failing = NewHarness();
        failing.Inject(FaultPoint.CopyData);
        await failing.RunAsync(Ct);

        foreach (var h in new[] { happy, failing })
        {
            var partition = h.Plan.SourceShrink!.Partition;
            partition.ShouldBe(WindowsPartition);
            h.Resizer.MinimumQueries.ShouldNotBeEmpty();
            h.Resizer.MinimumQueries.ShouldAllBe(queried => queried == partition);
            h.Resizer.Shrinks.ShouldNotBeEmpty();
            h.Resizer.Shrinks.ShouldAllBe(shrink => shrink.Partition == partition);
            h.Resizer.Restores.ShouldNotBeEmpty();
            h.Resizer.Restores.ShouldAllBe(restore => restore.Partition == partition);
        }
    }

    private static CompensationKind[] ExpectedCompensations(FaultPoint fault) => fault switch
    {
        FaultPoint.Snapshot or FaultPoint.PrepareTarget or FaultPoint.CopyData =>
            [CompensationKind.DeleteSnapshot, CompensationKind.RestoreSource, CompensationKind.AllowSleep],
        FaultPoint.CommitLayout =>
            [CompensationKind.DeleteTargetLayout, CompensationKind.DeleteSnapshot, CompensationKind.RestoreSource, CompensationKind.AllowSleep],
        FaultPoint.RepairBoot or FaultPoint.StructuralVerification =>
            [
                CompensationKind.DeleteTargetLayout,
                CompensationKind.DeleteSnapshot,
                CompensationKind.ReleaseMounts,
                CompensationKind.RestoreSource,
                CompensationKind.AllowSleep,
            ],
        _ => throw new ArgumentOutOfRangeException(nameof(fault), fault, null),
    };

    private static ExecuteCloneTests.Harness NewHarness() => new(ConfirmedShrinkPlan(), ShrinkInventory());
}
