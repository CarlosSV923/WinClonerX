using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>
/// Opens a device with <c>CreateFileW</c> for read and write, unbuffered and write-through.
/// Use only through <see cref="DiskWriteGuard"/>.
/// </summary>
internal sealed class Win32DeviceOpener : IDeviceOpener
{
    public SafeDeviceHandle OpenForReadWrite(string devicePath) =>
        Kernel32.CreateFileW(
            devicePath,
            Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_NO_BUFFERING | Kernel32.FILE_FLAG_WRITE_THROUGH,
            IntPtr.Zero);
}
