using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

public sealed class ProgressStepViewModelTests
{
    private static readonly ExecutionId Run = new(Guid.Parse("00000000-0000-0000-0000-0000000000e1"));
    private static readonly DateTimeOffset Now = new(2026, 10, 14, 10, 0, 0, TimeSpan.Zero);
    private static readonly PlanId Plan = new(Guid.Parse("00000000-0000-0000-0000-0000000000a1"));

    [Fact]
    public async Task PhasesFollowTheEvents()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        execute.Publish(new ExecutionStarted(Run, Now));
        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.Revalidate));
        vm.Phases.Count.ShouldBe(1);
        vm.Phases[0].State.ShouldBe(PhaseState.InProgress);

        execute.Publish(new PhaseCompleted(Run, Now, ExecutionPhase.Revalidate, TimeSpan.FromSeconds(1)));
        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.Snapshot));

        vm.Phases.Select(phase => phase.Phase).ShouldBe([ExecutionPhase.Revalidate, ExecutionPhase.Snapshot]);
        vm.Phases.Select(phase => phase.State).ShouldBe([PhaseState.Done, PhaseState.InProgress]);
        vm.Phases[0].StateText.ShouldBe(Loc.Get("Progress_State_Done"));
        vm.Phases[1].StateText.ShouldBe(Loc.Get("Progress_State_InProgress"));
        vm.CurrentPhaseText.ShouldBe(Loc.Format("Progress_CurrentPhase", Loc.Get("Phase_Snapshot")));

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public void PhaseNamesAreLocalized()
    {
        using var _ = new EnglishCulture();

        new PhaseItemViewModel(ExecutionPhase.CopyData).Name.ShouldBe("Copying data");
    }

    [Fact]
    public async Task ProgressShowsThePercentTheBytesAndTheThroughput()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        execute.Publish(Progress(bytesDone: 25 * Gib, bytesTotal: 100 * Gib, throughput: 50 * Mib, eta: TimeSpan.FromMinutes(5)));

        vm.Percent.ShouldBe(25.0);
        vm.PercentText.ShouldBe("25%");
        vm.BytesText.ShouldBe("25 GiB of 100 GiB");
        vm.ThroughputText.ShouldBe("50 MiB/s");

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public async Task BytesAreFormattedInTheSpanishLanguageToo()
    {
        using var _ = new CultureScope("es-ES");
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        execute.Publish(Progress(bytesDone: 25 * Gib, bytesTotal: 100 * Gib, throughput: 50 * Mib, eta: null));

        vm.BytesText.ShouldBe("25 GiB de 100 GiB");

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public async Task TheRemainingTimeSaysCalculatingWhileTheEtaIsUnknown()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        execute.Publish(Progress(bytesDone: 1 * Gib, bytesTotal: 100 * Gib, throughput: 50 * Mib, eta: null));

        vm.EtaText.ShouldContain("Calculating");
        vm.EtaText.ShouldBe(Loc.Format("Progress_Eta", Loc.Get("Progress_Eta_Calculating")));

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public async Task TheRemainingTimeIsShownOnceKnown()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        execute.Publish(Progress(bytesDone: 10 * Gib, bytesTotal: 100 * Gib, throughput: 50 * Mib, eta: TimeSpan.FromMinutes(5)));

        vm.EtaText.ShouldBe(Loc.Format("Progress_Eta", DurationText.Format(TimeSpan.FromMinutes(5))));
        vm.EtaText.ShouldContain("About 5 min");

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public async Task CompletionHandsTheResultReportToTheCaller()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);
        var report = Report();

        execute.Publish(new ExecutionCompleted(Run, Now));
        execute.Completion.SetResult(report);

        (await run).ShouldBeSameAs(report);
        execute.LastPlanId.ShouldBe(Plan);
        vm.IsRunning.ShouldBeFalse();
    }

    [Fact]
    public async Task EventsAfterTheRunAreIgnored()
    {
        using var _ = new EnglishCulture();
        var (vm, execute, _) = Create();
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);
        execute.Completion.SetResult(Report());
        await run;

        execute.ActiveObserverCount.ShouldBe(0);
        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.Cleanup));

        vm.Phases.ShouldBeEmpty();
    }

    [Fact]
    public async Task EventsAreMarshalledToTheUiThread()
    {
        using var _ = new EnglishCulture();
        var dispatcher = new TestUiDispatcher(deferred: true);
        var execute = new FakeExecuteClone();
        var vm = new ProgressStepViewModel(execute, dispatcher);
        var run = vm.StartAsync(Plan, TestContext.Current.CancellationToken);

        execute.Publish(new PhaseStarted(Run, Now, ExecutionPhase.Revalidate));
        vm.Phases.ShouldBeEmpty();

        dispatcher.Run();

        vm.Phases.Count.ShouldBe(1);

        execute.Completion.SetResult(Report());
        await run;
    }

    [Fact]
    public void CancelIsNotAvailableYet()
    {
        var (vm, _, _) = Create();

        vm.IsCancelAvailable.ShouldBeFalse();
    }

    private const long Mib = 1024L * 1024;
    private const long Gib = 1024L * 1024 * 1024;

    private static (ProgressStepViewModel Vm, FakeExecuteClone Execute, TestUiDispatcher Dispatcher) Create()
    {
        var dispatcher = new TestUiDispatcher();
        var execute = new FakeExecuteClone();
        return (new ProgressStepViewModel(execute, dispatcher), execute, dispatcher);
    }

    private static ProgressReported Progress(long bytesDone, long bytesTotal, double throughput, TimeSpan? eta) =>
        new(Run, Now, new CloneProgress(bytesDone, bytesTotal, throughput, eta));

    private static CloneResultReport Report(CloneOutcome outcome = CloneOutcome.Completed) => new(
        outcome,
        null,
        null,
        null,
        null,
        TimeSpan.FromMinutes(30),
        "log.clef",
        []);
}
