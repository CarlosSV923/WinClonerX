using System.ComponentModel;
using System.Runtime.InteropServices;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>
/// Win32 implementation of <see cref="IDriveLayoutReader"/>. <c>\\.\PhysicalDriveN</c> is opened with desiredAccess 0,
/// so the handle cannot read or write data. <c>IOCTL_DISK_GET_DRIVE_LAYOUT_EX</c> is FILE_ANY_ACCESS and only queries
/// the layout. The disk is never opened for writing here; that is <see cref="DiskWriteGuard"/>'s job alone.
/// </summary>
internal sealed class Win32DriveLayoutReader : IDriveLayoutReader
{
    public unsafe IReadOnlyList<DriveLayoutEntry> ReadLayout(int diskNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

        string path = PhysicalDisk.PathFor(diskNumber);
        using SafeDeviceHandle handle = Kernel32.CreateFileW(
            path,
            0,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open {path} for layout queries.");
        }

        byte[] reply = new byte[DriveLayoutParser.ReplyBufferSize];
        fixed (byte* replyPointer = reply)
        {
            bool ok = Kernel32.DeviceIoControl(
                handle,
                IoControlCodes.IOCTL_DISK_GET_DRIVE_LAYOUT_EX,
                null,
                0,
                replyPointer,
                (uint)reply.Length,
                out uint returned,
                IntPtr.Zero);

            if (!ok)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(),
                    $"IOCTL_DISK_GET_DRIVE_LAYOUT_EX failed for disk {diskNumber}.");
            }

            return DriveLayoutParser.Parse(reply.AsSpan(0, checked((int)returned)));
        }
    }
}
