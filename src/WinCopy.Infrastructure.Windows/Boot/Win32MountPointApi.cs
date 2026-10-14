using System.Runtime.InteropServices;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>Win32 implementation of <see cref="IMountPointApi"/> (SetVolumeMountPointW, DeleteVolumeMountPointW).</summary>
internal sealed class Win32MountPointApi : IMountPointApi
{
    public bool TrySetMountPoint(string mountRoot, string volumeGuidPath, out int win32Error)
    {
        bool ok = Kernel32.SetVolumeMountPointW(mountRoot, volumeGuidPath);
        win32Error = ok ? 0 : Marshal.GetLastPInvokeError();
        return ok;
    }

    public bool TryDeleteMountPoint(string mountRoot, out int win32Error)
    {
        bool ok = Kernel32.DeleteVolumeMountPointW(mountRoot);
        win32Error = ok ? 0 : Marshal.GetLastPInvokeError();
        return ok;
    }
}

/// <summary>Win32 implementation of <see cref="ILogicalDriveSource"/>.</summary>
internal sealed class EnvironmentLogicalDriveSource : ILogicalDriveSource
{
    public IReadOnlyList<string> GetLogicalDrives() => Environment.GetLogicalDrives();
}
