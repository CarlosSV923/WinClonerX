using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Win32 and registry implementation of <see cref="IVolumeExtentsReader"/>. Every device handle is opened with
/// desiredAccess 0, so no disk contents can be read or written. Every failure throws.
/// </summary>
internal sealed class WindowsVolumeExtentsReader : IVolumeExtentsReader
{
    private const int StorageDeviceProperty = 0;
    private const int PropertyStandardQuery = 0;
    private const int StorageQueryBufferSize = 1024;

    // STORAGE_DEVICE_DESCRIPTOR header ends at RawPropertiesLength (offset 32 + 4 bytes).
    private const uint DeviceDescriptorHeaderSize = 36;

    // VOLUME_DISK_EXTENTS: NumberOfDiskExtents (uint) plus padding to 8-byte alignment, then DISK_EXTENT[].
    private const int VolumeExtentsHeaderSize = 8;

    // Holds about 42 extents. A volume with more extents makes DeviceIoControl fail, which denies the write.
    private const int VolumeExtentsBufferSize = 1024;

    private const string SetupKeyPath = @"SYSTEM\Setup";
    private const string SystemPartitionValueName = "SystemPartition";
    private const string GlobalRootPrefix = @"\\?\GLOBALROOT";
    private const string DeviceNamespacePrefix = @"\Device\";

    public unsafe STORAGE_BUS_TYPE BusTypeOf(int diskNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

        using SafeDeviceHandle handle = OpenQueryOnly(PhysicalDisk.PathFor(diskNumber));

        var query = new STORAGE_PROPERTY_QUERY
        {
            PropertyId = StorageDeviceProperty,
            QueryType = PropertyStandardQuery,
        };
        byte[] output = new byte[StorageQueryBufferSize];

        uint returned = DeviceIoControl(
            handle, IoControlCodes.IOCTL_STORAGE_QUERY_PROPERTY, &query, (uint)sizeof(STORAGE_PROPERTY_QUERY), output);

        if (returned < DeviceDescriptorHeaderSize)
        {
            throw new InvalidOperationException(
                $"Storage descriptor for disk {diskNumber} is too short ({returned} bytes).");
        }

        return MemoryMarshal.Read<STORAGE_DEVICE_DESCRIPTOR>(output).BusType;
    }

    public unsafe IReadOnlySet<uint> DiskNumbersOf(string volumePath)
    {
        using SafeDeviceHandle handle = OpenQueryOnly(volumePath);

        byte[] output = new byte[VolumeExtentsBufferSize];
        uint returned = DeviceIoControl(
            handle, IoControlCodes.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS, null, 0, output);

        // Fail closed: a reply too short to hold its own header must not read as "no extents".
        if (returned < VolumeExtentsHeaderSize)
        {
            throw new InvalidOperationException($"Disk extents reply for '{volumePath}' is too short.");
        }

        uint extentCount = MemoryMarshal.Read<uint>(output);
        if (extentCount == 0)
        {
            throw new InvalidOperationException($"Volume '{volumePath}' reports no disk extents.");
        }

        int extentSize = Unsafe.SizeOf<DISK_EXTENT>();
        if (VolumeExtentsHeaderSize + (long)extentCount * extentSize > returned)
        {
            throw new InvalidOperationException($"Disk extents reply for '{volumePath}' is truncated.");
        }

        var disks = new HashSet<uint>();
        for (int i = 0; i < extentCount; i++)
        {
            int offset = VolumeExtentsHeaderSize + (i * extentSize);
            disks.Add(MemoryMarshal.Read<DISK_EXTENT>(output.AsSpan(offset)).DiskNumber);
        }

        return disks;
    }

    public string WindowsVolumePath()
    {
        string windowsFolder = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string root = Path.GetPathRoot(windowsFolder) ?? string.Empty;

        // Expected shape: "C:\"
        if (root.Length != 3 || !char.IsAsciiLetter(root[0]) || root[1] != ':' || root[2] != '\\')
        {
            throw new InvalidOperationException($"Unexpected Windows folder root '{root}'.");
        }

        return $@"\\.\{root[0]}:";
    }

    public string SystemPartitionVolumePath()
    {
        using RegistryKey? setup = Registry.LocalMachine.OpenSubKey(SetupKeyPath, writable: false);
        string? systemPartition = setup?.GetValue(SystemPartitionValueName) as string;

        if (string.IsNullOrWhiteSpace(systemPartition) ||
            !systemPartition.StartsWith(DeviceNamespacePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $@"HKLM\{SetupKeyPath}\{SystemPartitionValueName} is missing or is not a device path.");
        }

        return GlobalRootPrefix + systemPartition;
    }

    private static SafeDeviceHandle OpenQueryOnly(string path)
    {
        SafeDeviceHandle handle = Kernel32.CreateFileW(
            path,
            0,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open {path} for metadata queries.");
        }

        return handle;
    }

    private static unsafe uint DeviceIoControl(
        SafeDeviceHandle handle, uint controlCode, void* input, uint inputSize, byte[] output)
    {
        fixed (byte* outputPointer = output)
        {
            bool ok = Kernel32.DeviceIoControl(
                handle, controlCode, input, inputSize, outputPointer, (uint)output.Length, out uint returned, IntPtr.Zero);

            if (!ok)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(), $"DeviceIoControl 0x{controlCode:X8} failed.");
            }

            return returned;
        }
    }
}
