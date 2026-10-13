using System.Runtime.InteropServices;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.IntegrationTests.Spikes;

/// <summary>
/// File I/O imports used by spike S1. They only act on handles that come from
/// <see cref="WinCopy.Infrastructure.Windows.RawIo.DiskWriteGuard"/> (write) or from a read-only CreateFileW (read).
/// </summary>
internal static unsafe partial class SpikeNative
{
    public const uint FILE_BEGIN = 0;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetFilePointerEx(
        SafeDeviceHandle hFile,
        long liDistanceToMove,
        out long lpNewFilePointer,
        uint dwMoveMethod);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool WriteFile(
        SafeDeviceHandle hFile,
        byte* lpBuffer,
        uint nNumberOfBytesToWrite,
        out uint lpNumberOfBytesWritten,
        IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool ReadFile(
        SafeDeviceHandle hFile,
        byte* lpBuffer,
        uint nNumberOfBytesToRead,
        out uint lpNumberOfBytesRead,
        IntPtr lpOverlapped);
}
