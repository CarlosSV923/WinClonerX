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
/// Verificación completa (FR-022a) en el orquestador: la fase, su lugar en el orden, el informe y sus fallos. La lectura del
/// destino y la comprobación de digests corresponden al adaptador <see cref="ITargetVerifier"/>.
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
        h.TargetVerifier.Comparisons.ShouldBeEmpty();
        h.Copier.DigestSinkCalls.ShouldBe(0);
    }

    [Fact]
    public async Task FullVerificationRunsAfterTheCopyAndBeforeTheLayoutAndTheExpansion()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));

        await h.RunAsync(Ct);

        h.Phases().ShouldBe(FullPhases);
        h.Copier.DigestSinkCalls.ShouldBe(h.Plan.Mappings.Count);
        h.TargetVerifier.Comparisons.ShouldAllBe(comparison => ReferenceEquals(comparison.Target, h.Target) && comparison.Digests.Count > 0);
        h.Snapshots.CreatedSets.Single().Calls.Count(call => call == nameof(ISnapshotSet.OpenVolume))
            .ShouldBe(h.Plan.Mappings.Count(mapping => mapping.CopyMethod == CopyMethod.UsedClustersFromSnapshot));
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

        h.TargetVerifier.Comparisons.Select(comparison => comparison.TargetOffset)
            .ShouldBe(h.Plan.Mappings.Select(mapping => mapping.TargetExtent.Offset).ToArray());
        h.TargetVerifier.Comparisons.All(comparison => ReferenceEquals(comparison.Target, h.Target)).ShouldBeTrue();
    }

    [Fact]
    public async Task AnInjectedDifferenceInTheTargetFailsTheRunWithItsCountAndFirstOffsets()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        var windowsOffset = h.Plan.Mappings.Single(mapping => mapping.Source.Role == PartitionRole.Windows).TargetExtent.Offset;
        h.TargetVerifier.Overrides[windowsOffset] = new RangeComparison(
            1000,
            4096,
            [windowsOffset],
            1,
            [new ChunkMismatchSample(windowsOffset, 4096, true)]);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.Full.ShouldNotBeNull();
        report.Full.Passed.ShouldBeFalse();
        report.Full.DifferingBytes.ShouldBe(4096L);
        report.Full.FirstDifferenceOffsets!.ShouldBe(new[] { windowsOffset });
        report.Full.Issues.Count.ShouldBe(1);
        report.Full.Issues[0].ShouldContain("4096 bytes differ");
        var newGuid = h.Plan.Mappings.Single(mapping => mapping.Source.Role == PartitionRole.Windows).NewUniqueId.Value.ToString("D");
        report.Full.Issues[0].ShouldContain(newGuid);
        report.Full.Issues[0].ShouldNotContain("PartitionGuid");

        report.Failure.ShouldNotBeNull();
        report.Failure.Code.ShouldBe(PlatformErrorCode.VerificationFailed.ToString());
        report.Failure.Phase.ShouldBe(ExecutionPhase.FullVerification);

        h.Compensations().ShouldBe(NoLayoutCompensations);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
        h.Target.DisposeCount.ShouldBe(1);
        h.Power.IsSleepBlocked.ShouldBeFalse();
        h.Events.OfType<ExecutionFailed>().Count().ShouldBe(1);
        h.Log.Events.Single(e => e.Event == "FullVerificationCompleted").Level.ShouldBe(LogLevelKind.Error);
        var sampleLog = h.Log.Events.Single(e => e.Event == "FullVerificationMismatchSample");
        sampleLog.Properties["PartitionRole"].ShouldBe("Windows");
        sampleLog.Properties["NewPartitionGuid"].ShouldBe(newGuid);
        sampleLog.Properties["TargetOffset"].ShouldBe(windowsOffset);
        sampleLog.Properties["Length"].ShouldBe(4096);
        sampleLog.Properties["TargetStable"].ShouldBe(true);
    }

    [Fact]
    public async Task TheReportKeepsTheFirstTenDifferingOffsetsAcrossPartitions()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        long first = h.Plan.Mappings[0].TargetExtent.Offset;
        long second = h.Plan.Mappings[1].TargetExtent.Offset;
        h.TargetVerifier.Overrides[first] = new RangeComparison(4096, 4096, [first], 1);
        h.TargetVerifier.Overrides[second] = new RangeComparison(4096, 4096, [second], 1);

        var report = await h.RunAsync(Ct);

        report.Full.ShouldNotBeNull();
        report.Full.DifferingBytes.ShouldBe(8192L);
        report.Full.FirstDifferenceOffsets!.Count.ShouldBe(2);
        report.Full.FirstDifferenceOffsets.ShouldBe([first, second]);
    }

    [Fact]
    public async Task CancellingDuringTheFullVerificationEndsCancelledAndNeverCommitsTheLayout()
    {
        using var cts = new CancellationTokenSource();
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        h.TargetVerifier.OnChunk = cts.Cancel;

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
    public async Task ATargetReadErrorDuringVerificationFailsWithItsCodeAndNeverCommits()
    {
        var h = new ExecuteCloneTests.Harness(ConfirmedPlan(mode: VerificationMode.StructuralAndFull));
        h.TargetVerifier.FailOn[nameof(ITargetVerifier.VerifyAsync)] = new PlatformException(PlatformErrorCode.TargetWriteError, "read");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.Failure.ShouldNotBeNull();
        report.Failure.Code.ShouldBe(PlatformErrorCode.TargetWriteError.ToString());
        report.Failure.Phase.ShouldBe(ExecutionPhase.FullVerification);
        report.Full.ShouldBeNull();
        h.Compensations().ShouldBe(NoLayoutCompensations);
        h.Target.Calls.ShouldNotContain("CommitLayoutAsync");
    }
}
