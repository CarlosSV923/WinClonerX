using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.Platform;

/// <summary>Native power calls. The Win32 implementation is <see cref="Win32PowerNative"/>; tests use a fake.</summary>
internal interface IPowerNative
{
    /// <summary>Sets the execution state of the calling thread. Returns false when the call fails.</summary>
    bool TrySetThreadExecutionState(EXECUTION_STATE state);

    /// <summary>Reads the system power status. Returns false when the call fails.</summary>
    bool TryGetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}

/// <summary>Win32 implementation of <see cref="IPowerNative"/>.</summary>
internal sealed class Win32PowerNative : IPowerNative
{
    public bool TrySetThreadExecutionState(EXECUTION_STATE state)
    {
        // SetThreadExecutionState returns the previous state, or 0 on failure.
        return (uint)Kernel32.SetThreadExecutionState(state) != 0;
    }

    public bool TryGetSystemPowerStatus(out SYSTEM_POWER_STATUS status) =>
        Kernel32.GetSystemPowerStatus(out status);
}

/// <summary>
/// Keeps the system awake during a clone (R10, T072) and reads the battery state.
/// </summary>
/// <remarks>
/// <para>
/// The execution state belongs to the thread that sets it. All calls therefore run on one dedicated background thread,
/// which lives as long as this object. The first <see cref="PreventSleep"/> sets the state, and the last returned
/// lease resets it. Calls can nest.
/// </para>
/// <para>
/// Failure of the set call throws <see cref="PlatformException"/> and holds nothing. The clone must not start
/// without sleep protection.
/// </para>
/// </remarks>
internal sealed class PowerManager : IPowerManager, IDisposable
{
    internal const EXECUTION_STATE KeepSystemAwake = EXECUTION_STATE.ES_CONTINUOUS | EXECUTION_STATE.ES_SYSTEM_REQUIRED;
    internal const EXECUTION_STATE ClearRequirements = EXECUTION_STATE.ES_CONTINUOUS;

    private const byte UnknownBatteryPercent = 255;
    private const byte OnBatteryLineStatus = 0;

    private readonly IPowerNative _native;
    private readonly object _gate = new();
    private readonly BlockingCollection<Action> _work = new();
    private int _holders;
    private Thread? _thread;
    private bool _disposed;

    public PowerManager()
        : this(new Win32PowerNative())
    {
    }

    internal PowerManager(IPowerNative native)
    {
        ArgumentNullException.ThrowIfNull(native);

        _native = native;
    }

    /// <summary>Prevents sleep until the returned object is disposed. Nested calls share one state.</summary>
    public IDisposable PreventSleep()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_holders == 0)
            {
                RunOnStateThread(() =>
                {
                    if (!_native.TrySetThreadExecutionState(KeepSystemAwake))
                    {
                        throw new PlatformException(
                            PlatformErrorCode.Unknown,
                            "SetThreadExecutionState(ES_CONTINUOUS | ES_SYSTEM_REQUIRED) failed.");
                    }
                });
            }

            _holders++;
        }

        return new Lease(this);
    }

    /// <summary>Reads whether the machine runs on battery and how much charge is left.</summary>
    public Task<PowerStatus> GetStatusAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() => ReadStatus(), ct);
    }

    /// <summary>Stops the state thread. A sleep prevention still held is cleared first.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_thread is not null)
            {
                if (_holders > 0)
                {
                    _holders = 0;
                    RunOnStateThread(ClearStateIgnoringFailure);
                }

                _work.CompleteAdding();
                _thread.Join();
            }
        }

        _work.Dispose();
    }

    private PowerStatus ReadStatus()
    {
        if (!_native.TryGetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            throw new PlatformException(PlatformErrorCode.Unknown, "GetSystemPowerStatus failed.");
        }

        bool onBattery = status.ACLineStatus == OnBatteryLineStatus;
        int? percent = status.BatteryLifePercent == UnknownBatteryPercent ? null : status.BatteryLifePercent;

        return new PowerStatus(onBattery, percent);
    }

    private void Release()
    {
        lock (_gate)
        {
            if (_disposed || _holders == 0)
            {
                return;
            }

            _holders--;
            if (_holders == 0)
            {
                RunOnStateThread(ClearStateIgnoringFailure);
            }
        }
    }

    // Reset cannot be reported from Dispose, and a throw there would hide the error that caused the dispose.
    private void ClearStateIgnoringFailure() => _ = _native.TrySetThreadExecutionState(ClearRequirements);

    /// <summary>Runs <paramref name="work"/> on the state thread and waits for it. Rethrows its exception here.</summary>
    private void RunOnStateThread(Action work)
    {
        EnsureThread();

        using var done = new ManualResetEventSlim(initialState: false);
        Exception? failure = null;

        _work.Add(() =>
        {
            try
            {
                work();
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                done.Set();
            }
        });

        done.Wait();

        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }

    private void EnsureThread()
    {
        if (_thread is not null)
        {
            return;
        }

        _thread = new Thread(ProcessWork)
        {
            IsBackground = true,
            Name = "WinCopy.PowerManager",
        };
        _thread.Start();
    }

    private void ProcessWork()
    {
        foreach (Action work in _work.GetConsumingEnumerable())
        {
            work();
        }
    }

    /// <summary>One sleep prevention. Disposing it releases its share. A second Dispose does nothing.</summary>
    private sealed class Lease(PowerManager owner) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                owner.Release();
            }
        }
    }
}
