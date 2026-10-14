using WinCopy.Application.UseCases.Execution;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;

namespace WinCopy.App.Tests.Fakes;

/// <summary>
/// Execution fake: the test publishes the events, and the run finishes when the test completes <see cref="Completion"/>.
/// </summary>
internal sealed class FakeExecuteClone : IExecuteClone
{
    private readonly TestEventSubject _events = new();

    public IObservable<ExecutionEvent> Events => _events;

    public TaskCompletionSource<CloneResultReport> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public int CallCount { get; private set; }

    public PlanId? LastPlanId { get; private set; }

    public int ActiveObserverCount => _events.ActiveObserverCount;

    public void Publish(ExecutionEvent value) => _events.Publish(value);

    public Task<CloneResultReport> ExecuteAsync(PlanId planId, CancellationToken ct)
    {
        CallCount++;
        LastPlanId = planId;
        return Completion.Task;
    }
}
