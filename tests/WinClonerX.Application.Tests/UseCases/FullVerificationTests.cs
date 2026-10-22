using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Application.Tests.TestData;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using Xunit;
using static WinClonerX.Application.Tests.TestData.ExecuteCloneTestData;

namespace WinClonerX.Application.Tests.UseCases;

/// <summary>
/// Verificación completa (FR-022a) en el orquestador: la fase, su lugar en el orden, el informe y sus fallos. La comparación en sí
/// la hace el adaptador (<see cref="IVolumeComparer"/>); aquí se usa un fake que no lee datos y que devuelve lo que se le indica.
/// </summary>
public sealed class FullVerificationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>Fases de una ejecución con verificación completa y ampliación: la comparación va tras la copia y antes del diseño.</summary>
    private static readonly ExecutionPhase[] FullPhases =
    [
        ExecutionPhase.Revalidate,
        ExecutionPhase.Snapshot,
        ExecutionPhase.PrepareTarget,
        ExecutionPhase.CopyData,
        ExecutionPhase.FullVerification,
        ExecutionPhase.CommitLayout,
        ExecutionPhase.ExpandFileSystem,
        ExecutionPhase.RepairBoot,
        ExecutionPhase.StructuralVerification,
        ExecutionPhase.Cleanup,
    ];

    /// <summary>Compensaciones cuando falla o se cancela antes del diseño: sin diseño escrito no hay que borrarlo.</summary>
    private static readonly CompensationKind[] NoLayoutCompensations = [CompensationKind.DeleteSnapshot, CompensationKind.AllowSleep];

    [Fact]
    public async Task StructuralOnlyPlansNeverRunTheFullVerification()
    {
        var h = new ExecuteCloneTests.Harness();

        var report = await h.RunAsync(Ct);

        report.Full.ShouldBeNull();
        h.Phases().ShouldNotContain(ExecutionPhase.FullVerification);
        h.Comparer.Comparisons.ShouldBeEmpty();
    }

    [Fact]
    public async Task FullVerificationRunsAfterTheCopyAndBeforeTheLayoutAndTheExpansion()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));

        await h.RunAsync(Ct);

        h.Phases().ShouldBe(FullPhases);
    }

    [Fact]
    public async Task IdenticalDataPassesAndTheReportCarriesTheFullResult()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.Full.ShouldNotBeNull();
        report.Full.Passed.ShouldBeTrue();
        report.Full.DifferingBytes.ShouldBe(0L);
        report.Full.FirstDifferenceOffsets!.ShouldBeEmpty();
        report.Full.Issues.ShouldBeEmpty();
        h.Target.Calls.ShouldContain("CommitLayoutAsync");
        h.Events.OfType<ExecutionCompleted>().Count().ShouldBe(1);

        var logged = h.Log.Events.Single(e => e.Event == "FullVerificationCompleted");
        logged.Level.ShouldBe(LogLevelKind.Information);
        logged.Properties["Passed"].ShouldBe(true);
        logged.Properties["DifferingBytes"].ShouldBe(0L);
    }

    [Fact]
    public async Task EveryMappingIsComparedAtItsTargetExtentOnTheSameTargetDisk()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));

        await h.RunAsync(Ct);

        h.Comparer.Comparisons.Select(comparison => comparison.TargetOffset)
            .ShouldBe(h.Plan.Mappings.Select(mapping => mapping.TargetExtent.Offset).ToArray());
        h.Comparer.Comparisons.All(comparison => ReferenceEquals(comparison.Target, h.Target)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnInjectedDifferenceInTheTargetFailsTheRunWithItsCountAndFirstOffsets()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        var windowsOffset = h.Plan.Mappings.Single(mapping => mapping.Source.Role == PartitionRole.Windows).TargetExtent.Offset;
        h.Comparer.Overrides[windowsOffset] = new RangeComparison(1000, 3, [5, 4096, 8192]);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.Full.ShouldNotBeNull();
        report.Full.Passed.ShouldBeFalse();
        report.Full.DifferingBytes.ShouldBe(3L);
        report.Full.FirstDifferenceOffsets!.ShouldBe(new[] { windowsOffset + 5, windowsOffset + 4096, windowsOffset + 8192 });
        report.Full.Issues.Count.ShouldBe(1);
        report.Full.Issues[0].ShouldContain("3 bytes differ");

        report.Failure.ShouldNotBeNull();
        report.Failure.Code.ShouldBe(PlatformErrorCode.VerificationFailed.ToString());
        report.Failure.Phase.ShouldBe(ExecutionPhase.FullVerification);

        h.Compensations().ShouldBe(NoLayoutCompensations);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
        h.Target.DisposeCount.ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Events.OfType<ExecutionFailed>().Count().ShouldBe(1);
        h.Log.Events.Single(e => e.Event == "FullVerificationCompleted").Level.ShouldBe(LogLevelKind.Error);
    }

    [Fact]
    public async Task TheReportKeepsTheFirstTenDifferingOffsetsAcrossPartitions()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        long first = h.Plan.Mappings[0].TargetExtent.Offset;
        long second = h.Plan.Mappings[1].TargetExtent.Offset;
        h.Comparer.Overrides[first] = new RangeComparison(4096, 8, Enumerable.Range(0, 8).Select(i => (long)i).ToArray());
        h.Comparer.Overrides[second] = new RangeComparison(4096, 8, Enumerable.Range(100, 8).Select(i => (long)i).ToArray());

        var report = await h.RunAsync(Ct);

        report.Full.ShouldNotBeNull();
        report.Full.DifferingBytes.ShouldBe(16L);
        report.Full.FirstDifferenceOffsets!.Count.ShouldBe(10);
        report.Full.FirstDifferenceOffsets.Take(8).ShouldBe(Enumerable.Range(0, 8).Select(i => first + i).ToArray());
        report.Full.FirstDifferenceOffsets.Skip(8).ShouldBe(new[] { second + 100, second + 101 });
    }

    [Fact]
    public async Task CancellingDuringTheFullVerificationEndsCancelledAndNeverCommitsTheLayout()
    {
        using var cts = new CancellationTokenSource();
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        h.OnEvent = e =>
        {
            if (e is PhaseStarted { Phase: ExecutionPhase.FullVerification })
            {
                cts.Cancel();
            }
        };

        var report = await h.RunAsync(cts.Token);

        report.Outcome.ShouldBe(CloneOutcome.Cancelled);
        h.Phases().ShouldNotContain(ExecutionPhase.CommitLayout);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
        h.Compensations().ShouldBe(NoLayoutCompensations);
        h.Events.OfType<ExecutionFailed>().ShouldBeEmpty();
        h.Events.OfType<ExecutionCancelled>().Count().ShouldBe(1);
        h.Target.DisposeCount.ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task ASourceReadErrorDuringTheComparisonFailsWithItsCodeAndNeverCommits()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        h.Comparer.FailOn[nameof(IVolumeComparer.CompareAsync)] = new PlatformException(PlatformErrorCode.SourceReadError, "read");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.Failure.ShouldNotBeNull();
        report.Failure.Code.ShouldBe(PlatformErrorCode.SourceReadError.ToString());
        report.Failure.Phase.ShouldBe(ExecutionPhase.FullVerification);
        report.Full.ShouldBeNull();
        h.Compensations().ShouldBe(NoLayoutCompensations);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
    }
}
