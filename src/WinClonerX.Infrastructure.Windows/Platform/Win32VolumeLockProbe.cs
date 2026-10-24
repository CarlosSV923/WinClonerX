using System.ComponentModel;
using System.Runtime.InteropServices;
using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.Platform;

/// <summary>
/// Win32 implementation of <see cref="IVolumeLockProbe"/>. Opens the volume, takes FSCTL_LOCK_VOLUME and releases it at once.
/// </summary>
/// <remarks>
/// <para>
/// This is a probe, not a write path. The volume is opened with GENERIC_READ|GENERIC_WRITE only because
/// FSCTL_LOCK_VOLUME needs that access. The handle is a volume handle (<c>\\?\Volume{...}</c>), not a disk handle
/// (<c>\\.\PhysicalDriveN</c>), and it never writes a byte. FSCTL_LOCK_VOLUME only blocks other handles, and
/// FSCTL_UNLOCK_VOLUME runs in the same call, so no data is modified. The caller must never pass the volume of the
/// system disk; <see cref="PlatformFactsProvider"/> enforces that.
/// </para>
/// </remarks>
internal sealed class Win32VolumeLockProbe : IVolumeLockProbe
{
    public unsafe bool CanLockExclusively(string volumeDevicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeDevicePath);

        using SafeDeviceHandle handle = Kernel32.CreateFileW(
            volumeDevicePath,
            Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open volume {volumeDevicePath} for the lock probe.");
        }

        bool locked = false;
        try
        {
            // A refused lock means another handle is using the volume.
            locked = Kernel32.DeviceIoControl(
                handle, IoControlCodes.FSCTL_LOCK_VOLUME, null, 0, null, 0, out _, IntPtr.Zero);
            return locked;
        }
        finally
        {
            if (locked)
            {
                // Closing the handle also releases the lock, so an unlock failure here leaves nothing held.
                _ = Kernel32.DeviceIoControl(
                    handle, IoControlCodes.FSCTL_UNLOCK_VOLUME, null, 0, null, 0, out _, IntPtr.Zero);
            }
        }
    }
}
