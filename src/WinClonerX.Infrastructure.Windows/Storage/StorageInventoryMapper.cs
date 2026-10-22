using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>Everything read about one disk, before it becomes a domain <see cref="Disk"/>.</summary>
/// <param name="Disk">The <c>MSFT_Disk</c> row.</param>
/// <param name="Partitions">One entry per <c>MSFT_Partition</c> row of the disk.</param>
/// <param name="Layout">GPT entries of the disk, or empty when the disk is not GPT.</param>
internal sealed record DiskSnapshot(
    StorageDiskRecord Disk,
    IReadOnlyList<PartitionSnapshot> Partitions,
    IReadOnlyList<DriveLayoutEntry> Layout);

/// <summary>Everything read about one partition: its row, its volume GUID path and its volume row, if any.</summary>
internal sealed record PartitionSnapshot(
    StoragePartitionRecord Partition,
    string? VolumeGuidPath,
    StorageVolumeRecord? Volume);

/// <summary>
/// Pure mapping from raw Storage API and layout rows to domain objects. It reads no devices, so every rule here is
/// unit-tested. It throws <see cref="ArgumentException"/> or <see cref="OverflowException"/> for a disk it cannot
/// represent; the caller then skips that disk.
/// </summary>
internal static class StorageInventoryMapper
{
    /// <summary>Maps the <c>MSFT_Disk.BusType</c> code to the domain bus. Unknown codes are <see cref="BusKind.Other"/>.</summary>
    internal static BusKind MapBus(ushort busType) => busType switch
    {
        7 => BusKind.Usb,
        8 => BusKind.Raid,
        10 => BusKind.Sas,
        11 => BusKind.Sata,
        15 => BusKind.FileBackedVirtual,
        16 => BusKind.Spaces,
        17 => BusKind.Nvme,
        _ => BusKind.Other,
    };

    /// <summary>Maps <c>MSFT_Disk.PartitionStyle</c>: 0 RAW, 1 MBR, 2 GPT. Unknown codes are treated as RAW.</summary>
    internal static PartitionStyle MapPartitionStyle(ushort partitionStyle) => partitionStyle switch
    {
        2 => PartitionStyle.Gpt,
        1 => PartitionStyle.Mbr,
        _ => PartitionStyle.Raw,
    };

    /// <summary>Maps <c>MSFT_Volume.FileSystem</c>. An empty or missing name means the volume is unformatted (Raw).</summary>
    internal static FileSystemKind MapFileSystem(string? fileSystem)
    {
        if (string.IsNullOrWhiteSpace(fileSystem))
        {
            return FileSystemKind.Raw;
        }

        return fileSystem.Trim().ToUpperInvariant() switch
        {
            "NTFS" => FileSystemKind.Ntfs,
            "FAT32" => FileSystemKind.Fat32,
            "EXFAT" => FileSystemKind.ExFat,
            "REFS" => FileSystemKind.ReFs,
            "RAW" => FileSystemKind.Raw,
            _ => FileSystemKind.Other,
        };
    }

    /// <summary>
    /// Builds a volume. Used is Size minus SizeRemaining. A remaining count above the size is inconsistent data and
    /// reads as zero used, so the domain never sees Used above Capacity.
    /// </summary>
    internal static Volume BuildVolume(StorageVolumeRecord record, bool isWindowsVolume, EncryptionState encryption)
    {
        ulong used = record.SizeRemaining <= record.Size ? record.Size - record.SizeRemaining : 0;

        return new Volume(
            MapFileSystem(record.FileSystem),
            new ByteSize(checked((long)record.Size)),
            new ByteSize(checked((long)used)),
            record.AllocationUnitSize == 0 ? null : new ByteSize(record.AllocationUnitSize),
            encryption,
            isWindowsVolume,
            InUseByOthers: null);
    }

    /// <summary>
    /// Identity of a non-GPT partition. MBR and RAW partitions have no GPT GUID, so the identity is a SHA-256 of the
    /// disk number, partition number, offset and size. It is stable for one layout and never the GUID of a real
    /// partition. Non-GPT disks are unsupported as a source, and the identity only has to be unique.
    /// </summary>
    internal static Guid SyntheticPartitionGuid(uint diskNumber, uint partitionNumber, ulong offset, ulong size)
    {
        string key = string.Create(
            CultureInfo.InvariantCulture,
            $"winclonerx:non-gpt:{diskNumber}:{partitionNumber}:{offset}:{size}");

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return new Guid(hash.AsSpan(0, 16));
    }

    /// <summary>
    /// Builds a domain disk from its snapshot. Rules:
    /// <list type="bullet">
    /// <item>A GPT disk needs a disk GUID. A non-GPT disk uses the GUID when present, otherwise <see cref="Guid.Empty"/>.</item>
    /// <item>A GPT partition needs a partition GUID. A non-GPT partition gets a synthetic one (see <see cref="SyntheticPartitionGuid"/>).</item>
    /// <item>The GPT attributes and name come from the layout, matched by partition number. A missing entry reads as 0 and empty.</item>
    /// <item>A partition with no volume, such as the MSR, has <c>Volume = null</c>.</item>
    /// </list>
    /// </summary>
    internal static Disk BuildDisk(
        DiskSnapshot snapshot,
        bool isSystem,
        string windowsVolumeGuidPath,
        IReadOnlyDictionary<string, EncryptionState> encryption)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(encryption);

        StorageDiskRecord record = snapshot.Disk;
        PartitionStyle style = MapPartitionStyle(record.PartitionStyle);
        bool isGpt = style == PartitionStyle.Gpt;

        var partitions = new List<Partition>(snapshot.Partitions.Count);
        foreach (PartitionSnapshot partitionSnapshot in snapshot.Partitions)
        {
            partitions.Add(BuildPartition(record, isGpt, partitionSnapshot, snapshot.Layout, windowsVolumeGuidPath, encryption));
        }

        return new Disk(
            BuildDiskId(record, isGpt),
            (record.FriendlyName ?? string.Empty).Trim(),
            (record.SerialNumber ?? string.Empty).Trim(),
            new ByteSize(checked((long)record.Size)),
            new SectorSize(checked((int)record.LogicalSectorSize)),
            new SectorSize(checked((int)record.PhysicalSectorSize)),
            MapBus(record.BusType),
            style,
            isSystem,
            partitions,
            record.IsOffline,
            record.IsReadOnly);
    }

    private static DiskId BuildDiskId(StorageDiskRecord record, bool isGpt)
    {
        bool hasGuid = Guid.TryParse(record.Guid, out Guid guid) && guid != Guid.Empty;
        if (isGpt && !hasGuid)
        {
            throw new ArgumentException($"GPT disk {record.Number} has no disk GUID.", nameof(record));
        }

        return new DiskId(checked((int)record.Number), hasGuid ? guid : Guid.Empty);
    }

    private static Partition BuildPartition(
        StorageDiskRecord disk,
        bool isGpt,
        PartitionSnapshot snapshot,
        IReadOnlyList<DriveLayoutEntry> layout,
        string windowsVolumeGuidPath,
        IReadOnlyDictionary<string, EncryptionState> encryption)
    {
        StoragePartitionRecord partition = snapshot.Partition;

        var extent = new Extent(checked((long)partition.Offset), checked((long)partition.Size));
        var type = new GptPartitionType(isGpt ? ParseGuidOrEmpty(partition.GptType) : Guid.Empty);
        PartitionGuid uniqueId = isGpt
            ? new PartitionGuid(ParseRequiredGuid(partition.Guid, disk.Number, partition.PartitionNumber))
            : new PartitionGuid(SyntheticPartitionGuid(disk.Number, partition.PartitionNumber, partition.Offset, partition.Size));

        DriveLayoutEntry? entry = isGpt
            ? layout.FirstOrDefault(candidate => candidate.PartitionNumber == partition.PartitionNumber)
            : null;

        Volume? volume = null;
        if (snapshot.VolumeGuidPath is not null && snapshot.Volume is not null)
        {
            EncryptionState state = encryption.TryGetValue(VolumePath.Normalize(snapshot.VolumeGuidPath), out var known)
                ? known
                : EncryptionState.Unknown;

            bool isWindows = VolumePath.IsSameVolume(snapshot.VolumeGuidPath, windowsVolumeGuidPath);
            volume = BuildVolume(snapshot.Volume, isWindows, state);
        }

        return new Partition(
            checked((int)partition.PartitionNumber),
            extent,
            type,
            uniqueId,
            entry?.Attributes ?? 0,
            entry?.Name ?? string.Empty,
            volume);
    }

    private static Guid ParseGuidOrEmpty(string? text) =>
        Guid.TryParse(text, out Guid guid) ? guid : Guid.Empty;

    private static Guid ParseRequiredGuid(string? text, uint diskNumber, uint partitionNumber)
    {
        if (!Guid.TryParse(text, out Guid guid) || guid == Guid.Empty)
        {
            throw new ArgumentException(
                $"GPT partition {partitionNumber} of disk {diskNumber} has no partition GUID.", nameof(text));
        }

        return guid;
    }
}
