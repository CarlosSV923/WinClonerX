using System.Windows.Threading;
using WinClonerX.App.Services;
using WinClonerX.Application.Ports;
using WinClonerX.Application.UseCases;
using WinClonerX.Application.UseCases.Execution;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

/// <summary>Stores an information request so its dialog can be captured separately.</summary>
internal sealed class CapturedInfo : IInfoDialogService
{
    public InfoDialogRequest? LastRequest { get; private set; }

    public void Show(InfoDialogRequest request)
    {
        LastRequest = request;
    }
}

/// <summary>Returns the fixed inventory overview used by the disks screen.</summary>
internal sealed class FakeInventory(InventoryOverview overview) : IGetInventoryAndEligibility
{
    public Task<InventoryOverview> ExecuteAsync(CancellationToken cancellationToken) => Task.FromResult(overview);
}

/// <summary>Builds a deterministic plan from the screenshot inventory.</summary>
internal sealed class FakePlan(Func<VerificationMode, ClonePlan> build) : IBuildClonePlan
{
    public Task<Result<ClonePlan, PreflightReport>> ExecuteAsync(
        DiskId target,
        VerificationMode mode,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result<ClonePlan, PreflightReport>.Success(build(mode)));
}

/// <summary>Accepts the plan confirmation and returns the plan captured by the scenario.</summary>
internal sealed class FakeConfirm(Func<ClonePlan> getPlan) : IConfirmClonePlan
{
    public Task<ClonePlan> ExecuteAsync(
        PlanId id,
        IReadOnlyCollection<PreflightCode> warnings,
        string suffix,
        bool shrink,
        CancellationToken cancellationToken) => Task.FromResult(getPlan());
}

/// <summary>Runs posted UI actions immediately so view models update during capture.</summary>
internal sealed class InlineDispatcher : IUiDispatcher
{
    public void Post(Action action)
    {
        action();
    }
}

/// <summary>Declines cancellation confirmation because the capture always completes successfully.</summary>
internal sealed class NoConfirmation : IConfirmationService
{
    public bool Confirm(ConfirmationRequest request) => false;
}

/// <summary>Publishes synthetic execution events and completes the fake clone operation.</summary>
internal sealed class RecordingExecute : IExecuteClone
{
    private readonly EventStream _events = new();
    private readonly TaskCompletionSource<CloneResultReport> _completion = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    public IObservable<ExecutionEvent> Events => _events;
    public ManualResetEventSlim Started { get; } = new(false);

    public Task<CloneResultReport> ExecuteAsync(PlanId planId, CancellationToken cancellationToken)
    {
        Started.Set();
        return _completion.Task;
    }

    public void RequestCancel()
    {
    }

    public void WaitUntilStarted()
    {
        if (!Started.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("The fake execution did not start.");
        }
    }

    public void Publish(ExecutionEvent item)
    {
        _events.OnNext(item);
    }

    public void Complete(CloneResultReport report)
    {
        _completion.SetResult(report);
    }

    /// <summary>Maintains the observers subscribed to the synthetic execution stream.</summary>
    private sealed class EventStream : IObservable<ExecutionEvent>
    {
        private readonly List<IObserver<ExecutionEvent>> _observers = [];

        public IDisposable Subscribe(IObserver<ExecutionEvent> observer)
        {
            _observers.Add(observer);
            return new Unsubscribe(_observers, observer);
        }

        public void OnNext(ExecutionEvent value)
        {
            foreach (var observer in _observers.ToArray())
            {
                observer.OnNext(value);
            }
        }

        /// <summary>Removes a view model observer when it stops listening to execution events.</summary>
        private sealed class Unsubscribe(
            List<IObserver<ExecutionEvent>> observers,
            IObserver<ExecutionEvent> observer) : IDisposable
        {
            public void Dispose()
            {
                observers.Remove(observer);
            }
        }
    }
}

/// <summary>Supplies the fixed UTC time used by result view model actions.</summary>
internal sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow => ScreenshotScenario.FixedNow;
}

/// <summary>Accepts log export requests without writing files during capture.</summary>
internal sealed class NullExport : IExportLog
{
    public Task ExecuteAsync(string path, bool includeInventory, CancellationToken cancellationToken) =>
        Task.CompletedTask;
}

/// <summary>Leaves optional log export at its default selection.</summary>
internal sealed class NullFileDialog : IFileDialogService
{
    public string? PickExportFile(string name) => null;
}

/// <summary>Accepts folder open requests without launching a process during capture.</summary>
internal sealed class NullFolderOpener : IFolderOpener
{
    public void Open(string path)
    {
    }
}

/// <summary>Keeps the application open after displaying the final screenshot state.</summary>
internal sealed class NullLifetime : IAppLifetime
{
    public void Shutdown()
    {
    }
}

/// <summary>Drains pending WPF work so rendered controls reflect the latest view model state.</summary>
internal static class ScreenshotDispatcher
{
    public static void Flush()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.ApplicationIdle,
            new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }
}
