using System.Globalization;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Inventory;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.Platform;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Storage;

namespace WinClonerX.Infrastructure.Windows.Tests.Storage;

/// <summary>Row builders for the fake Storage API. The GUIDs and sizes model a typical single-disk machine.</summary>
internal static class StorageRows
{
    public const ulong Mib = 1UL << 20;
    public const ulong Gib = 1UL << 30;

    public const ushort BusUsb = 7;
    public const ushort BusNvme = 17;
    public const ushort StyleRaw = 0;
    public const ushort StyleMbr = 1;
    public const ushort StyleGpt = 2;

    public static readonly Guid EspType = new("C12A7328-F81F-11D2-BA4B-00A0C93EC93B");
    public static readonly Guid MsrType = new("E3C9E316-0B5C-4DB8-817D-F92DF00215AE");
    public static readonly Guid BasicType = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
    public static readonly Guid RecoveryType = new("DE94BBA4-06D1-4D40-A16A-BFD50179D6AC");
    public static readonly Guid UnknownType = new("12345678-1234-1234-1234-123456789ABC");

    public static readonly Guid SystemDiskGuid = new("702CA1D4-A415-4F7E-9D62-DA59090536B1");
    public static readonly Guid UsbDiskGuid = new("A1B2C3D4-0000-4000-8000-000000000001");

    public static string Braced(Guid guid) => guid.ToString("B", CultureInfo.InvariantCulture);

    /// <summary>The volume GUID path as WMI reports it: <c>\\?\Volume{guid}\</c>.</summary>
    public static string VolumePathOf(Guid volumeGuid) => @"\\?\Volume" + Braced(volumeGuid) + @"\";

    public static StorageDiskRecord GptDisk(uint number, Guid diskGuid, ushort bus = BusNvme, string? serial = "  SERIAL-0001  ") =>
        new(number, Braced(diskGuid), "Test Disk " + number.ToString(CultureInfo.InvariantCulture), serial,
            Size: 1000 * Gib, LogicalSectorSize: 512, PhysicalSectorSize: 4096, BusType: bus,
            PartitionStyle: StyleGpt, IsOffline: false, IsReadOnly: false);

    public static StoragePartitionRecord PartitionRow(uint number, ulong offset, ulong size, Guid? type, Guid? guid, params string[] accessPaths) =>
        new(number, offset, size, type is null ? null : Braced(type.Value), guid is null ? null : Braced(guid.Value), accessPaths);

    public static StorageVolumeRecord VolumeRow(string fileSystem, ulong size, ulong sizeRemaining, uint allocationUnit = 4096) =>
        new(fileSystem, size, sizeRemaining, allocationUnit);
}

/// <summary>Scripted <see cref="IStorageWmi"/>. Any row set can be replaced, and a failure can be scripted per call.</summary>
internal sealed class FakeStorageWmi : IStorageWmi
{
    public List<StorageDiskRecord> Disks { get; } = [];

    public Dictionary<uint, List<StoragePartitionRecord>> Partitions { get; } = [];

    public Dictionary<string, StorageVolumeRecord> Volumes { get; } = new(StringComparer.Ordinal);

    public Exception? DiskFailure { get; set; }

    public List<string> VolumeQueries { get; } = [];

    public IReadOnlyList<StorageDiskRecord> GetDisks()
    {
        if (DiskFailure is not null)
        {
            throw DiskFailure;
        }

        return Disks.ToArray();
    }

    public IReadOnlyList<StoragePartitionRecord> GetPartitions(uint diskNumber) =>
        Partitions.TryGetValue(diskNumber, out var rows) ? rows.ToArray() : [];

    public StorageVolumeRecord? GetVolume(string volumeGuidPath)
    {
        VolumeQueries.Add(volumeGuidPath);
        return Volumes.TryGetValue(volumeGuidPath, out var row) ? row : null;
    }
}

/// <summary>Scripted GPT layout reader. A disk in <see cref="Failing"/> throws, as a failed IOCTL would.</summary>
internal sealed class FakeDriveLayoutReader : IDriveLayoutReader
{
    public Dictionary<int, IReadOnlyList<DriveLayoutEntry>> Layouts { get; } = [];

    public HashSet<int> Failing { get; } = [];

    public List<int> Requested { get; } = [];

    public IReadOnlyList<DriveLayoutEntry> ReadLayout(int diskNumber)
    {
        Requested.Add(diskNumber);

        if (Failing.Contains(diskNumber))
        {
            throw new InvalidOperationException("IOCTL_DISK_GET_DRIVE_LAYOUT_EX failed (simulated).");
        }

        return Layouts.TryGetValue(diskNumber, out var entries) ? entries : [];
    }
}

internal sealed class FakeFirmwareInfo(FirmwareType firmware) : IFirmwareInfo
{
    public FirmwareType GetFirmwareType() => firmware;
}

internal sealed class FakeWindowsVolumeInfo(string? volumeGuidPath, Exception? failure = null) : IWindowsVolumeInfo
{
    public string WindowsVolumeGuidPath()
    {
        if (failure is not null)
        {
            throw failure;
        }

        return volumeGuidPath ?? throw new InvalidOperationException("No Windows volume (simulated).");
    }
}

/// <summary>Scripted volume extents: the disks behind each volume device path, as the IOCTL reports them.</summary>
internal sealed class FakeVolumeExtentsReader : IVolumeExtentsReader
{
    public string WindowsPath { get; set; } = @"\\.\C:";

    public string SystemPartitionPath { get; set; } = @"\\?\GLOBALROOT\Device\HarddiskVolume1";

    public Dictionary<string, HashSet<uint>> Extents { get; } = new(StringComparer.Ordinal);

    public IReadOnlySet<uint> DiskNumbersOf(string volumePath)
    {
        if (!Extents.TryGetValue(volumePath, out var disks))
        {
            throw new InvalidOperationException($"No extents scripted for '{volumePath}'.");
        }

        return disks;
    }

    public string WindowsVolumePath() => WindowsPath;

    public string SystemPartitionVolumePath() => SystemPartitionPath;

    public STORAGE_BUS_TYPE BusTypeOf(int diskNumber) => STORAGE_BUS_TYPE.BusTypeUnknown;
}

internal sealed class FixedClock(DateTimeOffset now) : IClock
{
    public DateTimeOffset UtcNow => now;
}

/// <summary>Scripted BitLocker WMI rows, or a failure.</summary>
internal sealed class FakeBitLockerWmi(IReadOnlyList<BitLockerVolumeRecord>? rows = null, Exception? failure = null) : IBitLockerQuery
{
    public int Calls { get; private set; }

    public IReadOnlyList<BitLockerVolumeRecord> GetVolumes()
    {
        Calls++;

        if (failure is not null)
        {
            throw failure;
        }

        return rows ?? [];
    }
}
