using System.Windows.Threading;

namespace WinCopy.App.Services;

/// <summary>
/// WPF implementation: posts to the dispatcher of the thread that created it (the UI thread, since the view models are
/// resolved while the main window is created).
/// </summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;

    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        _dispatcher.BeginInvoke(action);
    }
}
