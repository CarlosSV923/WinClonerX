using System.Runtime.InteropServices;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>
/// Win32 implementation of <see cref="IMountPointApi"/> (SetVolumeMountPointW, DeleteVolumeMountPointW,
/// GetVolumePathNamesForVolumeNameW).
/// </summary>
internal sealed class Win32MountPointApi : IMountPointApi
{
    // 261 is MAX_PATH + 1. A volume with more mount points makes the call fail with ERROR_MORE_DATA, and the retry uses the size it returns.
    private const int InitialPathNamesBufferLength = 261;
    private const int ErrorMoreData = 234;

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

    public unsafe bool TryGetVolumePathNames(string volumeGuidPath, out IReadOnlyList<string> mountPaths, out int win32Error)
    {
        uint length = InitialPathNamesBufferLength;

        while (true)
        {
            char[] buffer = new char[length];
            bool ok;
            uint returned = 0;
            int error = 0;

            fixed (char* pointer = buffer)
            {
                ok = Kernel32.GetVolumePathNamesForVolumeNameW(volumeGuidPath, pointer, length, out returned);
                if (!ok)
                {
                    error = Marshal.GetLastPInvokeError();
                }
            }

            if (ok)
            {
                mountPaths = ParseMultiString(buffer, checked((int)returned));
                win32Error = 0;
                return true;
            }

            // Retry only when the call says how much room it needs, and that is more than the buffer has.
            if (error != ErrorMoreData || returned <= length)
            {
                mountPaths = [];
                win32Error = error;
                return false;
            }

            length = returned;
        }
    }

    /// <summary>Splits a double-NUL-terminated multi-string. An empty entry ends the list.</summary>
    private static List<string> ParseMultiString(char[] buffer, int length)
    {
        var paths = new List<string>();
        int start = 0;

        for (int i = 0; i < length; i++)
        {
            if (buffer[i] != '\0')
            {
                continue;
            }

            if (i == start)
            {
                break;
            }

            paths.Add(new string(buffer, start, i - start));
            start = i + 1;
        }

        return paths;
    }
}

/// <summary>Win32 implementation of <see cref="ILogicalDriveSource"/>.</summary>
internal sealed class EnvironmentLogicalDriveSource : ILogicalDriveSource
{
    public IReadOnlyList<string> GetLogicalDrives() => Environment.GetLogicalDrives();
}
