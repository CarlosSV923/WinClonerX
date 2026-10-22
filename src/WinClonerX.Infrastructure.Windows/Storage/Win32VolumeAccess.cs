using System.ComponentModel;
using System.Runtime.InteropServices;
using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>
/// Win32 implementation of <see cref="IVolumeAccess"/>. Opens the volume with GENERIC_READ|GENERIC_WRITE and
/// FILE_SHARE_READ|FILE_SHARE_WRITE, which FSCTL_EXTEND_VOLUME needs. This is a volume handle, not a disk handle
/// (see <see cref="FileSystemExpander"/>).
/// </summary>
internal sealed class Win32VolumeAccess : IVolumeAccess
{
    public IVolumeSession OpenReadWrite(string volumeDevicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeDevicePath);

        SafeDeviceHandle handle = Kernel32.CreateFileW(
            volumeDevicePath,
            Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open volume {volumeDevicePath}.");
        }

        return new Win32VolumeSession(handle);
    }
}

/// <summary>Volume IOCTLs over one open volume handle.</summary>
internal sealed class Win32VolumeSession(SafeDeviceHandle handle) : IVolumeSession
{
    private const int Int64Bytes = sizeof(long);

    private readonly SafeDeviceHandle _handle = handle;

    public unsafe long GetLengthBytes() => Query<GET_LENGTH_INFORMATION>(IoControlCodes.IOCTL_DISK_GET_LENGTH_INFO).Length;

    public unsafe VolumeGeometry GetNtfsGeometry()
    {
        NTFS_VOLUME_DATA_BUFFER data = Query<NTFS_VOLUME_DATA_BUFFER>(IoControlCodes.FSCTL_GET_NTFS_VOLUME_DATA);
        return new VolumeGeometry(data.BytesPerSector, data.NumberSectors);
    }

    public unsafe void ExtendToSectors(long sectorCount)
    {
        // Input: the new size of the volume in sectors, as an 8-byte integer.
        long input = sectorCount;

        bool ok = Kernel32.DeviceIoControl(
            _handle,
            IoControlCodes.FSCTL_EXTEND_VOLUME,
            &input,
            Int64Bytes,
            null,
            0,
            out _,
            IntPtr.Zero);

        if (!ok)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"FSCTL_EXTEND_VOLUME to {sectorCount} sectors failed.");
        }
    }

    public void Dispose() => _handle.Dispose();

    private unsafe T Query<T>(uint controlCode)
        where T : unmanaged
    {
        T output = default;

        bool ok = Kernel32.DeviceIoControl(
            _handle,
            controlCode,
            null,
            0,
            &output,
            (uint)sizeof(T),
            out _,
            IntPtr.Zero);

        if (!ok)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(),
                $"DeviceIoControl 0x{controlCode:X8} failed.");
        }

        return output;
    }
}
