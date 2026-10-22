using System.Runtime.InteropServices;

namespace WinClonerX.Infrastructure.Windows.Interop;

internal static partial class Kernel32
{
    // CreateFileW access, share, disposition and flag values.
    internal const uint GENERIC_READ = 0x80000000;
    internal const uint GENERIC_WRITE = 0x40000000;
    internal const uint FILE_SHARE_READ = 0x00000001;
    internal const uint FILE_SHARE_WRITE = 0x00000002;
    internal const uint OPEN_EXISTING = 3;
    internal const uint FILE_FLAG_NO_BUFFERING = 0x20000000;
    internal const uint FILE_FLAG_WRITE_THROUGH = 0x80000000;

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    internal static partial SafeDeviceHandle CreateFileW(
        string lpFileName,
        uint dwDesiredAccess,
        uint dwShareMode,
        IntPtr lpSecurityAttributes,
        uint dwCreationDisposition,
        uint dwFlagsAndAttributes,
        IntPtr hTemplateFile);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool DeviceIoControl(
        SafeDeviceHandle hDevice,
        uint dwIoControlCode,
        void* lpInBuffer,
        uint nInBufferSize,
        void* lpOutBuffer,
        uint nOutBufferSize,
        out uint lpBytesReturned,
        IntPtr lpOverlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetFirmwareType(out FIRMWARE_TYPE FirmwareType);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    internal static partial EXECUTION_STATE SetThreadExecutionState(EXECUTION_STATE esFlags);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool SetVolumeMountPointW(string lpszVolumeMountPoint, string lpszVolumeName);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool DeleteVolumeMountPointW(string lpszVolumeMountPoint);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetVolumeNameForVolumeMountPointW(
        string lpszVolumeMountPoint,
        char* lpszVolumeName,
        uint cchBufferLength);

    [LibraryImport("kernel32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static unsafe partial bool GetVolumePathNamesForVolumeNameW(
        string lpszVolumeName,
        char* lpszVolumePathNames,
        uint cchBufferLength,
        out uint lpcchReturnLength);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool CloseHandle(IntPtr hObject);
}

internal enum FIRMWARE_TYPE
{
    Unknown = 0,
    Bios = 1,
    Uefi = 2,
    Max = 3,
}

[Flags]
internal enum EXECUTION_STATE : uint
{
    ES_SYSTEM_REQUIRED = 0x00000001,
    ES_CONTINUOUS = 0x80000000,
}
