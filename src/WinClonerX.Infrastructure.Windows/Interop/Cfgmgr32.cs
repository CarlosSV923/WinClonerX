using System.Runtime.InteropServices;

namespace WinClonerX.Infrastructure.Windows.Interop;

/// <summary>
/// Plug and Play notifications of cfgmgr32 (<c>CM_Register_Notification</c>). They need no window and no message loop: the
/// system calls the registered callback on its own thread.
/// </summary>
internal static partial class Cfgmgr32
{
    /// <summary>CR_SUCCESS: the call succeeded.</summary>
    internal const uint CR_SUCCESS = 0;

    /// <summary>CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE: notifications for a device interface class.</summary>
    internal const uint CM_NOTIFY_FILTER_TYPE_DEVICEINTERFACE = 0;

    /// <summary>CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL: an interface appeared. Every other action is a removal or a query.</summary>
    internal const uint CM_NOTIFY_ACTION_DEVICEINTERFACEARRIVAL = 0;

    /// <summary>GUID_DEVINTERFACE_DISK: the interface class of every disk.</summary>
    internal static readonly Guid GUID_DEVINTERFACE_DISK = new("53f56307-b6bf-11d0-94f2-00a0c91efb8b");

    /// <summary>Size of the native CM_NOTIFY_FILTER: 16 bytes of header plus its union, whose largest member is
    /// <c>WCHAR InstanceId[MAX_DEVICE_ID_LEN]</c> (200 characters, 400 bytes). <c>cbSize</c> must be exactly this, or
    /// CM_Register_Notification rejects the filter.</summary>
    internal const int CM_NOTIFY_FILTER_SIZE = 416;

    /// <summary>
    /// CM_NOTIFY_FILTER for a device interface class. The union member used here is the class GUID, but the struct keeps the
    /// size of the whole native union (<see cref="CM_NOTIFY_FILTER_SIZE"/>).
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Size = CM_NOTIFY_FILTER_SIZE)]
    internal struct CM_NOTIFY_FILTER
    {
        public uint cbSize;
        public uint Flags;
        public uint FilterType;
        public uint Reserved;
        public Guid ClassGuid;
    }

    /// <summary>
    /// Registers <paramref name="callback"/> for the interface class in <paramref name="filter"/>. The callback is a
    /// <c>CM_NOTIFY_CALLBACK</c> and must stay alive until <see cref="CM_Unregister_Notification"/> returns.
    /// </summary>
    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Register_Notification(
        ref CM_NOTIFY_FILTER filter,
        IntPtr context,
        IntPtr callback,
        out IntPtr notifyContext);

    /// <summary>Removes a registration. It waits for a callback that is running, so it must not be called from that callback.</summary>
    [LibraryImport("cfgmgr32.dll")]
    internal static partial uint CM_Unregister_Notification(IntPtr notifyContext);
}
