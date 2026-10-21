using System.Runtime.InteropServices;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.Platform;

/// <summary>
/// Win32 implementation of <see cref="IDeviceRemovalMonitor"/> (FR-011, US3 escenario 2). It registers for arrivals and
/// removals of disk device interfaces with <c>CM_Register_Notification</c>, so it needs no hidden window or message loop.
/// </summary>
/// <remarks>
/// <para>
/// The notification does not say which disk changed, so every watched disk is checked again on each removal (see
/// <see cref="DiskRemovalTracker"/>). Arrivals are ignored.
/// </para>
/// <para>
/// The callback runs on a system thread. It never throws: an exception escaping into native code would end the process. The
/// registration keeps the tracker alive through a <see cref="GCHandle"/>, which is freed only after the registration has been
/// removed, so no callback can see a freed handle.
/// </para>
/// </remarks>
internal sealed class DeviceChangeMonitor : IDeviceRemovalMonitor
{
    private readonly IDiskPresenceProbe _probe;

    /// <summary>Production instance: opens each disk to check it.</summary>
    public DeviceChangeMonitor()
        : this(new Win32DiskPresenceProbe())
    {
    }

    internal DeviceChangeMonitor(IDiskPresenceProbe probe)
    {
        ArgumentNullException.ThrowIfNull(probe);

        _probe = probe;
    }

    /// <inheritdoc />
    public IDisposable Watch(IReadOnlyCollection<int> diskNumbers, Action<DeviceChange> onRemoved)
    {
        ArgumentNullException.ThrowIfNull(diskNumbers);
        ArgumentNullException.ThrowIfNull(onRemoved);

        // The notification thread only queues the report. The reaction of the orchestrator (cancelling work, continuations)
        // runs on the thread pool, so CM_Unregister_Notification never waits for code that is waiting for it.
        var tracker = new DiskRemovalTracker(diskNumbers, _probe, change => _ = Task.Run(() => onRemoved(change)));
        return new Registration(tracker);
    }

    /// <summary>Callback of <c>CM_Register_Notification</c>. It must not throw: exceptions are swallowed here.</summary>
    [UnmanagedCallersOnly]
    private static uint OnInterfaceNotification(IntPtr notifyContext, IntPtr context, uint action, IntPtr eventData, uint eventDataSize)
    {
        try
        {
            if (action != Cfgmgr32.CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL
                && GCHandle.FromIntPtr(context).Target is DiskRemovalTracker tracker)
            {
                tracker.Check();
            }
        }
        catch (Exception)
        {
            // A failed check is not fatal: the next notification checks the disks again.
        }

        return Cfgmgr32.CR_SUCCESS;
    }

    private static unsafe IntPtr CallbackPointer() =>
        (IntPtr)(delegate* unmanaged<IntPtr, IntPtr, uint, IntPtr, uint, uint>)&OnInterfaceNotification;

    /// <summary>One <c>CM_Register_Notification</c> registration. Disposing it removes the registration and frees the context.</summary>
    private sealed class Registration : IDisposable
    {
        private readonly DiskRemovalTracker _tracker;
        private readonly GCHandle _context;
        private IntPtr _notification;
        private bool _disposed;

        public Registration(DiskRemovalTracker tracker)
        {
            _tracker = tracker;
            _context = GCHandle.Alloc(tracker);

            var filter = new Cfgmgr32.CM_NOTIFY_FILTER
            {
                cbSize = (uint)Marshal.SizeOf<Cfgmgr32.CM_NOTIFY_FILTER>(),
                FilterType = Cfgmgr32.CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE,
                ClassGuid = Cfgmgr32.GUID_DEVINTERFACE_DISK,
            };

            uint result = Cfgmgr32.CM_Register_Notification(
                ref filter,
                GCHandle.ToIntPtr(_context),
                CallbackPointer(),
                out _notification);

            if (result != Cfgmgr32.CR_SUCCESS)
            {
                _context.Free();
                throw new PlatformException(
                    PlatformErrorCode.Unknown,
                    $"CM_Register_Notification failed with configuration code {result}.");
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            // Stop first, so a callback that is already running does not report anything else. CM_Unregister_Notification
            // waits for a running callback to return, which is why the context is freed only after it.
            _tracker.Stop();
            uint result = Cfgmgr32.CM_Unregister_Notification(_notification);

            // If the registration could not be removed, the system may still call the callback: the context stays allocated
            // rather than leaving the callback a freed handle.
            if (result == Cfgmgr32.CR_SUCCESS)
            {
                _context.Free();
            }
        }
    }
}
