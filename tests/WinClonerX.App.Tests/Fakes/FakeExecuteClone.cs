using WinClonerX.Application.UseCases.Execution;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Planning;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>
/// Execution fake: the test publishes the events, and the run finishes when the test completes <see cref="Completion"/>.
/// </summary>
internal sealed class FakeExecuteClone : IExecuteClone
{
    private readonly TestEventSubject _events = new();
    private readonly object _callCountLock = new();
    private TaskCompletionSource _callCountChanged = NewSignal();
    private int _callCount;

    public IObservable<ExecutionEvent> Events => _events;

    public TaskCompletionSource<CloneResultReport> Completion { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount
    {
        get
        {
            lock (_callCountLock)
            {
                return _callCount;
            }
        }
    }

    public PlanId? LastPlanId { get; private set; }

    public int ActiveObserverCount => _events.ActiveObserverCount;

    /// <summary>Cancel requests received, in total.</summary>
    public int CancelRequests { get; private set; }

    public void Publish(ExecutionEvent value) => _events.Publish(value);

    public async Task WaitForCallCountAsync(int count, CancellationToken ct)
    {
        while (true)
        {
            Task wait;
            lock (_callCountLock)
            {
                if (_callCount >= count)
                {
                    return;
                }

                wait = _callCountChanged.Task;
            }

            await wait.WaitAsync(ct);
        }
    }

    public void RequestCancel() => CancelRequests++;

    public Task<CloneResultReport> ExecuteAsync(PlanId planId, CancellationToken ct)
    {
        if (CallCount > 0 && Completion.Task.IsCompleted)
        {
            Completion = new TaskCompletionSource<CloneResultReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        lock (_callCountLock)
        {
            _callCount++;
            _callCountChanged.TrySetResult();
            _callCountChanged = NewSignal();
        }

        LastPlanId = planId;
        return Completion.Task;
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
