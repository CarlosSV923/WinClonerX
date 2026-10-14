using System.Collections.Concurrent;
using System.Threading;
using WinCopy.Infrastructure.Windows.Interop.Vss;

namespace WinCopy.Infrastructure.Windows.Snapshots;

/// <summary>
/// A dedicated thread in the multithreaded apartment (MTA) that runs VSS work in order. Every COM object that VSS
/// creates is used only on this thread. Work items run one at a time and their results come back through a
/// <see cref="TaskCompletionSource{TResult}"/>.
/// </summary>
internal sealed class MtaWorker : IDisposable
{
    private readonly BlockingCollection<Action> _work = new();
    private readonly Thread _thread;
    private bool _disposed;

    internal MtaWorker()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "WinCopy VSS worker (MTA)" };
        _thread.SetApartmentState(ApartmentState.MTA);
        _thread.Start();
    }

    /// <summary>Runs <paramref name="work"/> on the worker thread.</summary>
    internal Task RunAsync(Action work) =>
        RunAsync(() =>
        {
            work();
            return true;
        });

    /// <summary>Runs <paramref name="work"/> on the worker thread and returns its result.</summary>
    internal Task<T> RunAsync<T>(Func<T> work)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        _work.Add(() =>
        {
            try
            {
                completion.SetResult(work());
            }
            catch (Exception ex)
            {
                // Any failure, including OperationCanceledException, reaches the awaiting caller.
                completion.SetException(ex);
            }
        });

        return completion.Task;
    }

    /// <summary>Stops the thread after the queued work has finished.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _work.CompleteAdding();
        _thread.Join();
        _work.Dispose();
    }

    private void Run()
    {
        // Join the MTA explicitly, and balance CoUninitialize only when the call succeeded.
        int hr = Ole32.CoInitializeEx(0, Ole32.COINIT_MULTITHREADED);
        try
        {
            foreach (Action item in _work.GetConsumingEnumerable())
            {
                item();
            }
        }
        finally
        {
            if (hr >= 0)
            {
                Ole32.CoUninitialize();
            }
        }
    }
}
