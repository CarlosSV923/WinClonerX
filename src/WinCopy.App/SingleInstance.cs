using System.Diagnostics;

namespace WinCopy.App;

/// <summary>
/// Keeps a single WinCopy process running at a time (FR-031) using a named mutex.
/// The mutex is held from <see cref="TryAcquire"/> until <see cref="Dispose"/>.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    /// <summary>Mutex name used by the application. The Global\ namespace covers every session.</summary>
    public const string DefaultMutexName = @"Global\WinCopy.SingleInstance";

    private readonly string _mutexName;
    private Mutex? _mutex;
    private bool _disposed;

    public SingleInstance()
        : this(DefaultMutexName)
    {
    }

    internal SingleInstance(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);
        _mutexName = mutexName;
    }

    /// <summary>
    /// Returns <c>true</c> when this instance is the first one and now owns the mutex,
    /// or <c>false</c> when another instance already holds it.
    /// </summary>
    public bool TryAcquire()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_mutex is not null)
        {
            return true;
        }

        // createdNew is false when the mutex already exists, whatever thread holds it. Using
        // WaitOne(0) instead would let the same thread re-enter the mutex and report success.
        var candidate = new Mutex(initiallyOwned: true, _mutexName, out bool createdNew);
        if (createdNew)
        {
            _mutex = candidate;
            return true;
        }

        candidate.Dispose();
        return false;
    }

    /// <summary>
    /// Brings the window of the already running WinCopy process to the front, restoring it when minimized.
    /// Does nothing when no other process with a visible window is found.
    /// </summary>
    public static void BringExistingInstanceToFront()
    {
        using var current = Process.GetCurrentProcess();

        foreach (var process in Process.GetProcessesByName(current.ProcessName))
        {
            using (process)
            {
                if (process.Id == current.Id)
                {
                    continue;
                }

                var handle = process.MainWindowHandle;
                if (handle == nint.Zero)
                {
                    continue;
                }

                if (NativeMethods.IsIconic(handle))
                {
                    NativeMethods.ShowWindow(handle, NativeMethods.SwRestore);
                }

                NativeMethods.SetForegroundWindow(handle);
                return;
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_mutex is not null)
        {
            _mutex.ReleaseMutex();
            _mutex.Dispose();
            _mutex = null;
        }
    }
}
