using System.Globalization;
using System.Management;
using WinClonerX.Application.Errors;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>
/// WMI implementation of <see cref="IStorageWmi"/>: <c>MSFT_Disk</c>, <c>MSFT_Partition</c> and <c>MSFT_Volume</c>
/// in <c>root\Microsoft\Windows\Storage</c>. Only SELECT queries are issued.
/// </summary>
internal sealed class WmiStorageInventory : IStorageWmi
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    private const string DiskQuery =
        "SELECT Number, Guid, FriendlyName, SerialNumber, Size, LogicalSectorSize, PhysicalSectorSize, " +
        "BusType, PartitionStyle, IsOffline, IsReadOnly FROM MSFT_Disk";

    private const string PartitionQueryPrefix =
        "SELECT PartitionNumber, Offset, Size, GptType, Guid, AccessPaths FROM MSFT_Partition WHERE DiskNumber = ";

    private const string VolumeQueryPrefix =
        "SELECT FileSystem, Size, SizeRemaining, AllocationUnitSize FROM MSFT_Volume WHERE Path = '";

    public IReadOnlyList<StorageDiskRecord> GetDisks() => Query(DiskQuery, ReadDisk);

    public IReadOnlyList<StoragePartitionRecord> GetPartitions(uint diskNumber) =>
        Query(PartitionQueryPrefix + diskNumber.ToString(CultureInfo.InvariantCulture), ReadPartition);

    public StorageVolumeRecord? GetVolume(string volumeGuidPath)
    {
        // The path is validated before it goes into WQL, so it cannot inject a clause.
        if (!VolumePath.IsVolumeGuidPath(volumeGuidPath))
        {
            throw new ArgumentException("Not a volume GUID path.", nameof(volumeGuidPath));
        }

        // WQL string literals need every backslash doubled.
        string wqlPath = volumeGuidPath.Replace("\\", "\\\\", StringComparison.Ordinal);
        List<StorageVolumeRecord> rows = Query(VolumeQueryPrefix + wqlPath + "'", ReadVolume);
        return rows.Count > 0 ? rows[0] : null;
    }

    private static List<T> Query<T>(string wql, Func<ManagementBaseObject, T> read)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(StorageScope, wql);
            using ManagementObjectCollection results = searcher.Get();

            var rows = new List<T>();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    rows.Add(read(item));
                }
            }

            return rows;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PlatformException(PlatformErrorCode.AccessDenied, "WMI denied the storage query.", ex);
        }
        catch (ManagementException ex)
        {
            throw new PlatformException(MapCode(ex), $"WMI storage query failed: {ex.Message}", ex);
        }
    }

    private static PlatformErrorCode MapCode(ManagementException ex) => ex.ErrorCode switch
    {
        ManagementStatus.InvalidNamespace or ManagementStatus.InvalidClass or ManagementStatus.NotFound
            => PlatformErrorCode.DeviceNotFound,
        ManagementStatus.AccessDenied => PlatformErrorCode.AccessDenied,
        _ => PlatformErrorCode.Unknown,
    };

    private static StorageDiskRecord ReadDisk(ManagementBaseObject item) => new(
        ReadUInt32(item, "Number"),
        ReadText(item, "Guid"),
        ReadText(item, "FriendlyName"),
        ReadText(item, "SerialNumber"),
        ReadUInt64(item, "Size"),
        ReadUInt32(item, "LogicalSectorSize"),
        ReadUInt32(item, "PhysicalSectorSize"),
        (ushort)ReadUInt32(item, "BusType"),
        (ushort)ReadUInt32(item, "PartitionStyle"),
        ReadBool(item, "IsOffline"),
        ReadBool(item, "IsReadOnly"));

    private static StoragePartitionRecord ReadPartition(ManagementBaseObject item) => new(
        ReadUInt32(item, "PartitionNumber"),
        ReadUInt64(item, "Offset"),
        ReadUInt64(item, "Size"),
        ReadText(item, "GptType"),
        ReadText(item, "Guid"),
        item["AccessPaths"] is string[] paths
            ? paths.Where(path => !string.IsNullOrEmpty(path)).ToArray()
            : Array.Empty<string>());

    private static StorageVolumeRecord ReadVolume(ManagementBaseObject item) => new(
        ReadText(item, "FileSystem"),
        ReadUInt64(item, "Size"),
        ReadUInt64(item, "SizeRemaining"),
        ReadUInt32(item, "AllocationUnitSize"));

    // Convert.To* returns zero, false or an empty string for a null WMI property, so a missing value never throws.
    private static uint ReadUInt32(ManagementBaseObject item, string name) =>
        Convert.ToUInt32(item[name], CultureInfo.InvariantCulture);

    private static ulong ReadUInt64(ManagementBaseObject item, string name) =>
        Convert.ToUInt64(item[name], CultureInfo.InvariantCulture);

    private static bool ReadBool(ManagementBaseObject item, string name) =>
        Convert.ToBoolean(item[name], CultureInfo.InvariantCulture);

    private static string? ReadText(ManagementBaseObject item, string name)
    {
        string text = Convert.ToString(item[name], CultureInfo.InvariantCulture) ?? string.Empty;
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }
}
