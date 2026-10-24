using System.Globalization;
using System.Management;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>What the Storage Management API reports about one GPT partition (<c>MSFT_Partition</c>).</summary>
/// <param name="DiskNumber">Disk number that holds the partition.</param>
/// <param name="PartitionNumber">Partition number on that disk.</param>
/// <param name="Offset">Byte offset of the partition on its disk.</param>
/// <param name="Size">Current length of the partition in bytes.</param>
/// <param name="AccessPaths">Drive letter paths and volume GUID paths mounted on the partition.</param>
/// <param name="DriveLetter">Drive letter assigned to the partition, or null when it has none.</param>
internal sealed record PartitionResizeInfo(
    int DiskNumber,
    int PartitionNumber,
    long Offset,
    long Size,
    IReadOnlyList<string> AccessPaths,
    char? DriveLetter);

/// <summary>
/// The Storage Management calls needed to shrink and restore a partition. The production implementation is
/// <see cref="WmiPartitionResizeApi"/>; unit tests use a fake, so no test resizes anything.
/// </summary>
internal interface IPartitionResizeApi
{
    /// <summary>Reads the partition with GPT unique id <paramref name="partitionGuid"/>. Throws unless exactly one matches.</summary>
    PartitionResizeInfo GetPartition(Guid partitionGuid);

    /// <summary>Calls <c>MSFT_Partition.GetSupportedSize</c>: the smallest and largest lengths Windows accepts.</summary>
    (long SizeMin, long SizeMax) GetSupportedSize(int diskNumber, int partitionNumber);

    /// <summary>
    /// Calls <c>MSFT_Partition.Resize</c>. The call is synchronous and can take minutes, because Windows moves files
    /// to make room. It cannot be cancelled once it has started.
    /// </summary>
    void Resize(int diskNumber, int partitionNumber, long newSize);
}

/// <summary>
/// WMI implementation of <see cref="IPartitionResizeApi"/> on <c>root\Microsoft\Windows\Storage</c>. Every method throws
/// when WMI fails or when a method returns a non-zero <c>ReturnValue</c>. The caller maps those exceptions.
/// </summary>
internal sealed class WmiPartitionResizeApi : IPartitionResizeApi
{
    private const string StorageScope = @"root\Microsoft\Windows\Storage";

    public PartitionResizeInfo GetPartition(Guid partitionGuid)
    {
        // A .NET-formatted GUID holds only hex digits, hyphens and braces, so it cannot inject WQL.
        string guid = partitionGuid.ToString("B", CultureInfo.InvariantCulture).ToUpperInvariant();
        string query =
            "SELECT DiskNumber, PartitionNumber, Offset, Size, AccessPaths, DriveLetter " +
            $"FROM MSFT_Partition WHERE Guid = '{guid}'";

        using var searcher = new ManagementObjectSearcher(StorageScope, query);
        using ManagementObjectCollection results = searcher.Get();

        var found = new List<PartitionResizeInfo>();
        foreach (ManagementBaseObject item in results)
        {
            using (item)
            {
                found.Add(ReadInfo(item));
            }
        }

        if (found.Count != 1)
        {
            throw new InvalidOperationException(
                $"Expected exactly one MSFT_Partition for {guid}, found {found.Count}.");
        }

        return found[0];
    }

    public (long SizeMin, long SizeMax) GetSupportedSize(int diskNumber, int partitionNumber) =>
        WithPartition(diskNumber, partitionNumber, partition =>
        {
            using ManagementBaseObject inParameters = partition.GetMethodParameters("GetSupportedSize");
            using ManagementBaseObject output = partition.InvokeMethod("GetSupportedSize", inParameters, null);

            ThrowIfFailed(output, "GetSupportedSize");

            return (
                Convert.ToInt64(output["SizeMin"], CultureInfo.InvariantCulture),
                Convert.ToInt64(output["SizeMax"], CultureInfo.InvariantCulture));
        });

    public void Resize(int diskNumber, int partitionNumber, long newSize)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(newSize);

        WithPartition(diskNumber, partitionNumber, partition =>
        {
            using ManagementBaseObject inParameters = partition.GetMethodParameters("Resize");
            inParameters["Size"] = (ulong)newSize;

            using ManagementBaseObject output = partition.InvokeMethod("Resize", inParameters, null);

            ThrowIfFailed(output, "Resize");
            return 0;
        });
    }

    private static PartitionResizeInfo ReadInfo(ManagementBaseObject item)
    {
        string[] accessPaths = item["AccessPaths"] as string[] ?? [];

        return new PartitionResizeInfo(
            DiskNumber: Convert.ToInt32(item["DiskNumber"], CultureInfo.InvariantCulture),
            PartitionNumber: Convert.ToInt32(item["PartitionNumber"], CultureInfo.InvariantCulture),
            Offset: Convert.ToInt64(item["Offset"], CultureInfo.InvariantCulture),
            Size: Convert.ToInt64(item["Size"], CultureInfo.InvariantCulture),
            AccessPaths: accessPaths,
            DriveLetter: ReadDriveLetter(item["DriveLetter"]));
    }

    /// <summary>MSFT_Partition.DriveLetter is a UTF-16 code unit, and 0 means "no drive letter".</summary>
    private static char? ReadDriveLetter(object? value)
    {
        if (value is null)
        {
            return null;
        }

        ushort code = Convert.ToUInt16(value, CultureInfo.InvariantCulture);
        return code == 0 ? null : (char)code;
    }

    /// <summary>
    /// Finds the single MSFT_Partition with the given disk and partition numbers and runs <paramref name="action"/> on it.
    /// The instance comes from a query, so its methods are available without guessing the WMI key layout.
    /// </summary>
    private static T WithPartition<T>(int diskNumber, int partitionNumber, Func<ManagementObject, T> action)
    {
        string query =
            "SELECT * FROM MSFT_Partition " +
            $"WHERE DiskNumber = {diskNumber.ToString(CultureInfo.InvariantCulture)} " +
            $"AND PartitionNumber = {partitionNumber.ToString(CultureInfo.InvariantCulture)}";

        using var searcher = new ManagementObjectSearcher(StorageScope, query);
        using ManagementObjectCollection results = searcher.Get();

        var matches = new List<ManagementObject>();
        try
        {
            foreach (ManagementBaseObject item in results)
            {
                matches.Add(item as ManagementObject
                    ?? throw new InvalidOperationException("MSFT_Partition returned a non-instance object."));
            }

            if (matches.Count != 1)
            {
                throw new InvalidOperationException(
                    $"Expected exactly one MSFT_Partition for disk {diskNumber}, partition {partitionNumber}, found {matches.Count}.");
            }

            return action(matches[0]);
        }
        finally
        {
            foreach (ManagementObject match in matches)
            {
                match.Dispose();
            }
        }
    }

    /// <summary>A non-zero ReturnValue is a failure. ExtendedStatus, when present, says why.</summary>
    private static void ThrowIfFailed(ManagementBaseObject output, string method)
    {
        uint returnValue = Convert.ToUInt32(output["ReturnValue"], CultureInfo.InvariantCulture);
        if (returnValue == 0)
        {
            return;
        }

        string extended = ReadOptional(output, "ExtendedStatus") ?? "none";
        throw new InvalidOperationException(
            $"MSFT_Partition.{method} returned {returnValue} (ExtendedStatus: {extended}).");
    }

    private static string? ReadOptional(ManagementBaseObject output, string name)
    {
        try
        {
            return output[name]?.ToString();
        }
        catch (ManagementException)
        {
            return null;
        }
    }
}
