using System.ComponentModel;
using System.Runtime.InteropServices;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>Win32 implementation of <see cref="IWindowsVolumeInfo"/>, using <c>GetVolumeNameForVolumeMountPointW</c>.</summary>
internal sealed class Win32WindowsVolumeInfo : IWindowsVolumeInfo
{
    // A volume GUID path is 49 characters; 64 leaves room and still holds the terminator.
    private const int VolumeNameCapacity = 64;

    public unsafe string WindowsVolumeGuidPath()
    {
        string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string root = Path.GetPathRoot(windowsFolder) ?? string.Empty;

        // Expected shape: "C:\"
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
        {
            throw new InvalidOperationException($"Unexpected Windows folder root '{root}'.");
        }

        char[] buffer = new char[VolumeNameCapacity];
        fixed (char* bufferPointer = buffer)
        {
            if (!Kernel32.GetVolumeNameForVolumeMountPointW(root, bufferPointer, (uint)buffer.Length))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not resolve the volume of {root}.");
            }
        }

        int length = Array.IndexOf(buffer, '\0');
        string path = new(buffer, 0, length < 0 ? buffer.Length : length);

        if (!VolumePath.IsVolumeGuidPath(path))
        {
            throw new InvalidOperationException($"Unexpected volume name '{path}' for {root}.");
        }

        return path;
    }
}
