using System.ComponentModel;
using System.Runtime.InteropServices;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>Parameters of one <c>CreateFileW</c> call made by <see cref="RawPartitionReader"/>.</summary>
internal sealed record RawDeviceOpenRequest(
    string DevicePath,
    uint DesiredAccess,
    uint ShareMode,
    uint CreationDisposition,
    uint Flags);

/// <summary>Opens a device as an <see cref="IRawDevice"/>. Throws <see cref="Win32Exception"/> when the OS refuses.</summary>
internal interface IRawDeviceOpener
{
    IRawDevice Open(RawDeviceOpenRequest request);
}

/// <summary>
/// Win32 opener. It refuses any request that asks for write access, so this path can never write to a disk.
/// </summary>
internal sealed class Win32RawDeviceOpener : IRawDeviceOpener
{
    public IRawDevice Open(RawDeviceOpenRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if ((request.DesiredAccess & Kernel32.GENERIC_WRITE) != 0)
        {
            throw new InvalidOperationException("The raw reader never opens a device for writing.");
        }

        SafeDeviceHandle handle = Kernel32.CreateFileW(
            request.DevicePath,
            request.DesiredAccess,
            request.ShareMode,
            IntPtr.Zero,
            request.CreationDisposition,
            request.Flags,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open {request.DevicePath} for reading.");
        }

        return new Win32RawDevice(handle);
    }
}

/// <summary>
/// Opens a partition range of a physical disk for raw, read-only copy (T062). The disk is opened with GENERIC_READ
/// only, so no code path here can write to it. Writes are not part of this adapter. It does not use
/// <see cref="DiskWriteGuard"/>, because that guard governs write handles only.
/// </summary>
internal sealed class RawPartitionReader : IRawPartitionReader
{
    private const uint ReadOnlyAccess = Kernel32.GENERIC_READ;
    private const uint SharedAccess = Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE;
    private const uint RawReadFlags = Kernel32.FILE_FLAG_NO_BUFFERING;

    private const int ErrorAccessDenied = 5;
    private const int ErrorFileNotFound = 2;
    private const int ErrorPathNotFound = 3;

    private readonly IRawDeviceOpener _opener;

    public RawPartitionReader()
        : this(new Win32RawDeviceOpener())
    {
    }

    internal RawPartitionReader(IRawDeviceOpener opener)
    {
        ArgumentNullException.ThrowIfNull(opener);

        _opener = opener;
    }

    /// <summary>Opens <c>\\.\PhysicalDriveN</c> read-only and returns the block source for <paramref name="extent"/>.</summary>
    public IBlockSource Open(DiskId source, Extent extent)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(source.Number);

        if (extent.Offset % RawPartitionSource.SectorBytes != 0 || extent.Length % RawPartitionSource.SectorBytes != 0)
        {
            throw new ArgumentException("The extent must be aligned to the sector size.", nameof(extent));
        }

        string path = PhysicalDisk.PathFor(source.Number);
        var request = new RawDeviceOpenRequest(
            path,
            ReadOnlyAccess,
            SharedAccess,
            Kernel32.OPEN_EXISTING,
            RawReadFlags);

        IRawDevice device;
        try
        {
            device = _opener.Open(request);
        }
        catch (Win32Exception ex)
        {
            throw new PlatformException(MapOpenError(ex.NativeErrorCode), $"Could not open {path} for reading.", ex);
        }

        // The Read of the log names the extent it reads: its offsets are relative to the start of the extent.
        return new LabeledBlockSource(
            new RawPartitionSource(device, extent),
            $"{TargetDiskFactory.DiskLabel(source)}, extent at {extent.Offset}");
    }

    private static PlatformErrorCode MapOpenError(int nativeError) => nativeError switch
    {
        ErrorAccessDenied => PlatformErrorCode.AccessDenied,
        ErrorFileNotFound or ErrorPathNotFound => PlatformErrorCode.DeviceNotFound,
        _ => PlatformErrorCode.SourceReadError,
    };
}
