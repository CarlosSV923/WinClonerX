using WinCopy.App.Services;

namespace WinCopy.App.Tests.Fakes;

/// <summary>
/// UI dispatcher for tests. Runs posted actions at once by default. With <c>deferred</c> it queues them until
/// <see cref="Run"/>, which lets a test check that work is marshalled to the UI thread.
/// </summary>
internal sealed class TestUiDispatcher(bool deferred = false) : IUiDispatcher
{
    private readonly Queue<Action> _queue = new();

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (deferred)
        {
            _queue.Enqueue(action);
        }
        else
        {
            action();
        }
    }

    /// <summary>Runs every queued action in order.</summary>
    public void Run()
    {
        while (_queue.Count > 0)
        {
            _queue.Dequeue()();
        }
    }
}
