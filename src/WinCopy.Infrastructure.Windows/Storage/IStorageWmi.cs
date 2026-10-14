namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>One <c>MSFT_Disk</c> row. Fields keep the WMI types; <see cref="StorageInventoryMapper"/> converts them.</summary>
internal sealed record StorageDiskRecord(
    uint Number,
    string? Guid,
    string? FriendlyName,
    string? SerialNumber,
    ulong Size,
    uint LogicalSectorSize,
    uint PhysicalSectorSize,
    ushort BusType,
    ushort PartitionStyle,
    bool IsOffline,
    bool IsReadOnly);

/// <summary>One <c>MSFT_Partition</c> row. <see cref="AccessPaths"/> lists the mount points of the partition.</summary>
internal sealed record StoragePartitionRecord(
    uint PartitionNumber,
    ulong Offset,
    ulong Size,
    string? GptType,
    string? Guid,
    IReadOnlyList<string> AccessPaths);

/// <summary>One <c>MSFT_Volume</c> row, found by its volume GUID path.</summary>
internal sealed record StorageVolumeRecord(string? FileSystem, ulong Size, ulong SizeRemaining, uint AllocationUnitSize);

/// <summary>Reads the Storage Management API (<c>root\Microsoft\Windows\Storage</c>). Read-only queries only.</summary>
internal interface IStorageWmi
{
    /// <summary>Every disk the Storage API reports.</summary>
    IReadOnlyList<StorageDiskRecord> GetDisks();

    /// <summary>The partitions of disk <paramref name="diskNumber"/>.</summary>
    IReadOnlyList<StoragePartitionRecord> GetPartitions(uint diskNumber);

    /// <summary>The volume at <paramref name="volumeGuidPath"/>, or null when no volume matches.</summary>
    StorageVolumeRecord? GetVolume(string volumeGuidPath);
}
