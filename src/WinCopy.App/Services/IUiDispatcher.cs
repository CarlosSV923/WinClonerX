namespace WinCopy.App.Services;

/// <summary>
/// Runs work on the UI thread. Execution events are published from a background thread, so view models post them through
/// this abstraction. Tests replace it with a synchronous implementation.
/// </summary>
public interface IUiDispatcher
{
    /// <summary>Queues <paramref name="action"/> to run on the UI thread, in the order it was posted.</summary>
    void Post(Action action);
}
