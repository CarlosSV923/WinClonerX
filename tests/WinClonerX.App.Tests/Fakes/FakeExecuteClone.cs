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

    public IObservable<ExecutionEvent> Events => _events;

    public TaskCompletionSource<CloneResultReport> Completion { get; private set; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public PlanId? LastPlanId { get; private set; }

    public int ActiveObserverCount => _events.ActiveObserverCount;

    /// <summary>Cancel requests received, in total.</summary>
    public int CancelRequests { get; private set; }

    public void Publish(ExecutionEvent value) => _events.Publish(value);

    public void RequestCancel() => CancelRequests++;

    public Task<CloneResultReport> ExecuteAsync(PlanId planId, CancellationToken ct)
    {
        if (Completion.Task.IsCompleted)
        {
            Completion = new TaskCompletionSource<CloneResultReport>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        CallCount++;
        LastPlanId = planId;
        return Completion.Task;
    }
}
