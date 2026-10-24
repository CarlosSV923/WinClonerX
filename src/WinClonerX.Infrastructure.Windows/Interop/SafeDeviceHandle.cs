using Microsoft.Win32.SafeHandles;

namespace WinClonerX.Infrastructure.Windows.Interop;

/// <summary>
/// Owns a handle returned by <c>CreateFileW</c> and releases it with <c>CloseHandle</c>.
/// </summary>
internal sealed class SafeDeviceHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    // Parameterless constructor is required by LibraryImport for SafeHandle return values.
    public SafeDeviceHandle() : base(ownsHandle: true)
    {
    }

    protected override bool ReleaseHandle() => Kernel32.CloseHandle(handle);
}
