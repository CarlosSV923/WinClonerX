using System.Runtime.InteropServices;

namespace WinCopy.Infrastructure.Windows.Interop;

/// <summary>
/// Token privileges and offline registry hive calls. Used only by <c>OfflineRegistryEditor</c>, which edits the
/// registry of a clone and never the running system.
/// </summary>
internal static partial class Advapi32
{
    internal const uint TOKEN_ADJUST_PRIVILEGES = 0x0020;
    internal const uint TOKEN_QUERY = 0x0008;
    internal const uint SE_PRIVILEGE_ENABLED = 0x00000002;

    /// <summary>Returned by AdjustTokenPrivileges when the named privilege is not held by the token.</summary>
    internal const int ERROR_NOT_ALL_ASSIGNED = 1300;

    /// <summary>
    /// Pseudo handle of the current process (the value of GetCurrentProcess). It needs no CloseHandle.
    /// </summary>
    internal static readonly IntPtr CurrentProcessPseudoHandle = new(-1);

    /// <summary>HKEY_LOCAL_MACHINE. The 32-bit value is sign-extended, as the Windows headers define it.</summary>
    internal static readonly IntPtr HKEY_LOCAL_MACHINE = new(unchecked((int)0x80000002));

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [LibraryImport("advapi32.dll", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool LookupPrivilegeValueW(string? systemName, string name, out LUID luid);

    [LibraryImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static partial bool AdjustTokenPrivileges(
        IntPtr tokenHandle,
        [MarshalAs(UnmanagedType.Bool)] bool disableAllPrivileges,
        in TOKEN_PRIVILEGES newState,
        uint bufferLength,
        IntPtr previousState,
        IntPtr returnLength);

    /// <summary>Loads the hive file into the registry under <paramref name="subKey"/>. Returns a Win32 error code, 0 on success.</summary>
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegLoadKeyW(IntPtr hKey, string subKey, string file);

    /// <summary>Unloads a hive loaded with RegLoadKeyW. Returns a Win32 error code, 0 on success.</summary>
    [LibraryImport("advapi32.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial int RegUnLoadKeyW(IntPtr hKey, string subKey);
}

[StructLayout(LayoutKind.Sequential)]
internal struct LUID
{
    public uint LowPart;
    public int HighPart;
}

/// <summary>TOKEN_PRIVILEGES with exactly one entry, which is all this code adjusts (16 bytes).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct TOKEN_PRIVILEGES
{
    public uint PrivilegeCount;
    public LUID Luid;
    public uint Attributes;
}
