using System.Globalization;
using Shouldly;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Application.Tests.Fakes;
using WinCopy.Application.Tests.TestData;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;
using FaultPoint = WinCopy.Application.Tests.UseCases.ExecuteCloneTests.FaultPoint;
using static WinCopy.Application.Tests.TestData.ShrinkTestData;

namespace WinCopy.Application.Tests.UseCases;

/// <summary>
/// Diario de restauración del origen en la orquestación (FR-021a): se escribe antes de reducir, se borra solo cuando el origen
/// tiene su longitud original (camino feliz o compensación) y se conserva si la restauración falla. Un plan sin reducción
/// nunca lo toca. Usa el mismo arnés que <see cref="ExecuteCloneTests"/>.
/// </summary>
public sealed class ExecuteCloneJournalTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TheJournalIsWrittenWithThePlanValuesBeforeTheShrinkStarts()
    {
        var h = NewHarness();

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        var shrink = h.Plan.SourceShrink!;
        var entry = h.Journal.Recorded.Single();
        entry.Partition.ShouldBe(shrink.Partition);
        entry.OriginalLength.ShouldBe(shrink.OriginalLength);
        entry.Plan.ShouldBe(h.Plan.Id);
        entry.RecordedAt.ShouldBe(FakeDefaults.Now);
        h.Journal.ShrinksBeforeEachRecord.Single().ShouldBe(0);
        h.Resizer.Shrinks.Count.ShouldBe(1);
        h.Log.Events.Count(e => e.Event == "SourceRestoreJournalWritten").ShouldBe(1);
    }

    [Fact]
    public async Task AJournalWriteFailureAbortsBeforeShrinkingAndNeverTouchesTheSource()
    {
        var h = NewHarness();
        h.Journal.FailOn[nameof(ISourceRestoreJournal.Record)] = new IOException("disk full");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        var failure = h.Events.OfType<ExecutionFailed>().Single().Failure;
        failure.Code.ShouldBe("ShrinkJournalFailed");
        failure.Phase.ShouldBe(ExecutionPhase.ShrinkSource);
        h.Resizer.Calls.ShouldNotContain("ShrinkAsync");
        h.Resizer.Shrinks.ShouldBeEmpty();
        h.Resizer.Restores.ShouldBeEmpty();
        h.Targets.OpenedPlans.ShouldBeEmpty();
        h.Journal.Entry.ShouldBeNull();
        h.Compensations().ShouldBe(new[] { CompensationKind.RestoreSource, CompensationKind.AllowSleep });
        h.Power.IsSleepBlocked.ShouldBeFalse();
    }

    [Fact]
    public async Task APendingEntryFromAnEarlierRunBlocksTheShrinkAndIsNotOverwritten()
    {
        var h = NewHarness();
        var earlier = new PendingSourceRestore(WindowsPartition, ByteSize.FromGiB(256), new PlanId(Guid.NewGuid()), FakeDefaults.Now);
        h.Journal.Entry = earlier;

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        h.Events.OfType<ExecutionFailed>().Single().Failure.Code.ShouldBe("ShrinkJournalFailed");
        h.Resizer.Calls.ShouldNotContain("ShrinkAsync");
        h.Journal.Recorded.ShouldBeEmpty();
        h.Journal.Entry.ShouldBe(earlier);
        h.Journal.Calls.ShouldNotContain("Clear");
    }

    [Fact]
    public async Task ASuccessfulRunClearsTheJournalOnlyAfterTheSourceIsBackAtItsLength()
    {
        var h = NewHarness();

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.SourceRestored.ShouldBe(true);
        h.Journal.Entry.ShouldBeNull();
        h.Journal.Cleared.ShouldBe(1);
        // En el momento del borrado la restauración ya se había pedido: el diario no se borra antes.
        h.Journal.RestoresBeforeEachClear.Single().ShouldBe(1);
        h.Log.Events.Count(e => e.Event == "SourceRestoreJournalCleared").ShouldBe(1);
    }

    [Fact]
    public async Task TheRestoreCompensationClearsTheJournalWhenItRestoresTheSource()
    {
        var h = NewHarness();
        h.Inject(FaultPoint.CopyData);

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.SourceRestored.ShouldBe(true);
        h.Resizer.Restores.Count.ShouldBe(1);
        h.Journal.Entry.ShouldBeNull();
        h.Journal.Cleared.ShouldBe(1);
    }

    [Fact]
    public async Task AFailedRestoreOnTheHappyPathKeepsTheJournalForTheNextStart()
    {
        var h = NewHarness();
        h.Resizer.FailOn[nameof(ISourceResizer.RestoreAsync)] = new InvalidOperationException("vds restore busy");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.SourceRestored.ShouldBe(false);
        h.Journal.Entry.ShouldNotBeNull();
        h.Journal.Entry!.Partition.ShouldBe(h.Plan.SourceShrink!.Partition);
        h.Journal.Entry.OriginalLength.ShouldBe(h.Plan.SourceShrink!.OriginalLength);
        h.Journal.Calls.ShouldNotContain("Clear");
        h.Journal.Cleared.ShouldBe(0);
    }

    [Fact]
    public async Task AFailedRestoreCompensationKeepsTheJournal()
    {
        var h = NewHarness();
        h.Inject(FaultPoint.CopyData);
        h.Resizer.FailOn[nameof(ISourceResizer.RestoreAsync)] = new InvalidOperationException("vds restore busy");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Failed);
        report.SourceRestored.ShouldBe(false);
        h.Journal.Entry.ShouldNotBeNull();
        h.Journal.Cleared.ShouldBe(0);
    }

    [Fact]
    public async Task AFailedJournalClearIsAWarningAndTheRunStillCompletes()
    {
        var h = NewHarness();
        h.Journal.FailOn[nameof(ISourceRestoreJournal.Clear)] = new IOException("file locked");

        var report = await h.RunAsync(Ct);

        report.Outcome.ShouldBe(CloneOutcome.Completed);
        report.SourceRestored.ShouldBe(true);
        h.Events.OfType<ExecutionFailed>().ShouldBeEmpty();
        var warning = h.Log.Events.Single(e => e.Event == "SourceRestoreJournalClearFailed");
        warning.Level.ShouldBe(LogLevelKind.Warning);
        warning.Properties["Partition"].ShouldBe(h.Plan.SourceShrink!.Partition.Value.ToString("D", CultureInfo.InvariantCulture));
        warning.Properties.Keys.Intersect(LogEvent.ReservedPropertyNames).ShouldBeEmpty();
    }

    [Fact]
    public async Task APlanWithoutShrinkNeverTouchesTheJournal()
    {
        var happy = new ExecuteCloneTests.Harness();
        var failing = new ExecuteCloneTests.Harness();
        failing.Inject(FaultPoint.CopyData);

        var happyReport = await happy.RunAsync(Ct);
        var failingReport = await failing.RunAsync(Ct);

        happy.Plan.SourceShrink.ShouldBeNull();
        happyReport.Outcome.ShouldBe(CloneOutcome.Completed);
        happy.Journal.Calls.ShouldBeEmpty();
        failing.Plan.SourceShrink.ShouldBeNull();
        failingReport.Outcome.ShouldBe(CloneOutcome.Failed);
        failing.Journal.Calls.ShouldBeEmpty();
    }

    private static ExecuteCloneTests.Harness NewHarness() => new(ConfirmedShrinkPlan(), ShrinkInventory());
}
