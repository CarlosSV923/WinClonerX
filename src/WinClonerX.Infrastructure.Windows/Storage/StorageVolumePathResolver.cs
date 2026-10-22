using System.Globalization;
using WinClonerX.Application.Errors;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>
/// Reads the access paths of a partition from the Storage Management API. The production implementation is
/// <see cref="WmiStorageQuery"/>; tests use a fake.
/// </summary>
internal interface IStorageQuery
{
    /// <summary>
    /// Returns the access paths of the partition whose GPT unique id is <paramref name="partitionGuid"/>.
    /// Returns an empty list when the partition is unknown.
    /// </summary>
    IReadOnlyList<string> GetAccessPaths(Guid partitionGuid);
}

/// <summary>
/// Translates a GPT partition into the Windows volume GUID path that lives on it (for example
/// <c>\\?\Volume{...}\</c>), using the <c>AccessPaths</c> of <c>MSFT_Partition</c>.
/// </summary>
internal sealed class StorageVolumePathResolver : IVolumePathResolver
{
    private const string VolumeGuidPrefix = @"\\?\Volume{";
    private const char TrailingSeparator = '\\';

    private readonly IStorageQuery _query;

    public StorageVolumePathResolver()
        : this(new WmiStorageQuery())
    {
    }

    internal StorageVolumePathResolver(IStorageQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);

        _query = query;
    }

    /// <summary>
    /// Returns the first access path that starts with <c>\\?\Volume{</c> and ends with a backslash. Throws
    /// <see cref="PlatformErrorCode.DeviceNotFound"/> when there is none.
    /// </summary>
    public string GetVolumeGuidPath(PartitionGuid partition)
    {
        IReadOnlyList<string> accessPaths = _query.GetAccessPaths(partition.Value);

        foreach (string path in accessPaths)
        {
            if (IsVolumeGuidPath(path))
            {
                return path;
            }
        }

        throw new PlatformException(
            PlatformErrorCode.DeviceNotFound,
            $"No volume GUID path is mounted for partition {partition.Value.ToString("B", CultureInfo.InvariantCulture)}.");
    }

    private static bool IsVolumeGuidPath(string? path) =>
        path is not null
        && path.StartsWith(VolumeGuidPrefix, StringComparison.OrdinalIgnoreCase)
        && path.EndsWith(TrailingSeparator);
}
