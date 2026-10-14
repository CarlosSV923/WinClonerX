using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Platform;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>
/// Builds the <see cref="DiskInventory"/> from the Storage Management API, the GPT layout, the firmware type, BitLocker
/// and the volume extents. Read-only: every device is opened without write access, if at all.
/// </summary>
/// <remarks>
/// <para><b>IsSystem</b> uses the same rule as <see cref="DiskWriteGuard"/>: the disk holds the Windows volume or the boot
/// EFI system partition. Both come from <see cref="IVolumeExtentsReader"/>, and any failure there fails the read.</para>
/// <para><b>Disks that cannot be represented</b> (an unsupported sector size, a GPT layout that cannot be read, or a
/// partition without a usable GUID) are skipped with a warning and never throw. A skipped system disk makes the inventory
/// unsupported (<c>NoSystemDisk</c>), so the clone is still blocked.</para>
/// <para><b>Errors</b> from WMI, the layout, the extents or the Windows volume lookup are raised as
/// <see cref="PlatformException"/>, with the technical detail in the message.</para>
/// </remarks>
internal sealed class StorageInventoryReader : IDiskInventoryReader
{
    private readonly IStorageWmi _wmi;
    private readonly IDriveLayoutReader _layout;
    private readonly IFirmwareInfo _firmware;
    private readonly BitLockerQuery _bitLocker;
    private readonly IWindowsVolumeInfo _windowsVolume;
    private readonly IVolumeExtentsReader _extents;
    private readonly IClock _clock;
    private readonly Action<string> _warn;

    public StorageInventoryReader(
        IStorageWmi wmi,
        IDriveLayoutReader layout,
        IFirmwareInfo firmware,
        BitLockerQuery bitLocker,
        IWindowsVolumeInfo windowsVolume,
        IVolumeExtentsReader extents,
        IClock clock,
        Action<string>? warn = null)
    {
        ArgumentNullException.ThrowIfNull(wmi);
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(firmware);
        ArgumentNullException.ThrowIfNull(bitLocker);
        ArgumentNullException.ThrowIfNull(windowsVolume);
        ArgumentNullException.ThrowIfNull(extents);
        ArgumentNullException.ThrowIfNull(clock);

        _wmi = wmi;
        _layout = layout;
        _firmware = firmware;
        _bitLocker = bitLocker;
        _windowsVolume = windowsVolume;
        _extents = extents;
        _clock = clock;
        _warn = warn ?? (static _ => { });
    }

    /// <inheritdoc />
    public Task<DiskInventory> ReadAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() => Read(ct), ct);
    }

    private DiskInventory Read(CancellationToken ct)
    {
        try
        {
            FirmwareType firmware = _firmware.GetFirmwareType();
            HashSet<uint> systemDisks = SystemDiskNumbers();
            string windowsVolumePath = _windowsVolume.WindowsVolumeGuidPath();

            var snapshots = new List<DiskSnapshot>();
            foreach (StorageDiskRecord disk in _wmi.GetDisks())
            {
                ct.ThrowIfCancellationRequested();

                DiskSnapshot? snapshot = ReadDisk(disk);
                if (snapshot is not null)
                {
                    snapshots.Add(snapshot);
                }
            }

            IReadOnlyDictionary<string, EncryptionState> encryption = _bitLocker.GetStates(
                snapshots
                    .SelectMany(snapshot => snapshot.Partitions)
                    .Select(partition => partition.VolumeGuidPath)
                    .OfType<string>());

            var disks = new List<Disk>(snapshots.Count);
            foreach (DiskSnapshot snapshot in snapshots)
            {
                bool isSystem = systemDisks.Contains(snapshot.Disk.Number);
                Disk? built = TryBuild(snapshot, isSystem, windowsVolumePath, encryption);
                if (built is not null)
                {
                    disks.Add(built);
                }
            }

            return new DiskInventory(_clock.UtcNow, firmware, disks);
        }
        catch (Exception ex) when (ex is not PlatformException and not OperationCanceledException)
        {
            throw new PlatformException(
                PlatformErrorCode.Unknown,
                $"Could not read the disk inventory: {ex.Message}",
                ex);
        }
    }

    /// <summary>The disk numbers of the Windows volume plus those of the boot EFI partition (the DiskWriteGuard rule).</summary>
    private HashSet<uint> SystemDiskNumbers()
    {
        var numbers = new HashSet<uint>(_extents.DiskNumbersOf(_extents.WindowsVolumePath()));
        numbers.UnionWith(_extents.DiskNumbersOf(_extents.SystemPartitionVolumePath()));
        return numbers;
    }

    /// <summary>Reads the rows of one disk. Returns null when the disk must be skipped (its GPT layout is unreadable).</summary>
    private DiskSnapshot? ReadDisk(StorageDiskRecord disk)
    {
        var partitions = new List<PartitionSnapshot>();
        foreach (StoragePartitionRecord partition in _wmi.GetPartitions(disk.Number))
        {
            string? volumePath = partition.AccessPaths.FirstOrDefault(VolumePath.IsVolumeGuidPath);
            StorageVolumeRecord? volume = volumePath is null ? null : _wmi.GetVolume(volumePath);
            partitions.Add(new PartitionSnapshot(partition, volumePath, volume));
        }

        // Only GPT disks have a layout. Reading one for a RAW or MBR disk (a card reader with no media, for example)
        // would fail for no reason.
        IReadOnlyList<DriveLayoutEntry> layout = [];
        if (StorageInventoryMapper.MapPartitionStyle(disk.PartitionStyle) == PartitionStyle.Gpt)
        {
            try
            {
                layout = _layout.ReadLayout(checked((int)disk.Number));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _warn($"Disk {disk.Number} skipped: its GPT layout could not be read ({ex.Message}).");
                return null;
            }
        }

        return new DiskSnapshot(disk, partitions, layout);
    }

    private Disk? TryBuild(
        DiskSnapshot snapshot,
        bool isSystem,
        string windowsVolumePath,
        IReadOnlyDictionary<string, EncryptionState> encryption)
    {
        try
        {
            return StorageInventoryMapper.BuildDisk(snapshot, isSystem, windowsVolumePath, encryption);
        }
        catch (Exception ex) when (ex is ArgumentException or OverflowException)
        {
            _warn($"Disk {snapshot.Disk.Number} skipped: {ex.Message}");
            return null;
        }
    }
}
