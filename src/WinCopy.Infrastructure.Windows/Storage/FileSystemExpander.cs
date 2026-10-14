using System.Globalization;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Logging;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>Geometry of an NTFS volume, from FSCTL_GET_NTFS_VOLUME_DATA.</summary>
internal readonly record struct VolumeGeometry(long BytesPerSector, long NumberSectors);

/// <summary>An open volume handle, with the three calls the expander needs. Tests replace it with a fake.</summary>
internal interface IVolumeSession : IDisposable
{
    /// <summary>Length of the volume in bytes (IOCTL_DISK_GET_LENGTH_INFO on the volume).</summary>
    long GetLengthBytes();

    /// <summary>Sector size and sector count of the NTFS volume (FSCTL_GET_NTFS_VOLUME_DATA).</summary>
    VolumeGeometry GetNtfsGeometry();

    /// <summary>Grows the volume to <paramref name="sectorCount"/> sectors (FSCTL_EXTEND_VOLUME).</summary>
    void ExtendToSectors(long sectorCount);
}

/// <summary>Opens a volume device path. The Win32 implementation is <see cref="Win32VolumeAccess"/>.</summary>
internal interface IVolumeAccess
{
    IVolumeSession OpenReadWrite(string volumeDevicePath);
}

/// <summary>
/// Expands the NTFS volume of a partition to fill its partition (FSCTL_EXTEND_VOLUME, R6, T066). Used after the
/// clone has been committed, so the copy of the system partition can grow to the target disk's size.
/// </summary>
/// <remarks>
/// <para>
/// The volume handle is not a disk handle. FSCTL_EXTEND_VOLUME works on the volume (<c>\\?\Volume{...}</c>), so
/// this path does not go through <see cref="DiskWriteGuard"/>. The guard protects physical disks
/// (constitution principle I), and the rule about the system disk is checked before the disk is written, not here.
/// </para>
/// <para>
/// Each run is recorded as one <c>DiskOperation</c> with <c>Operation = Extend</c>.
/// </para>
/// </remarks>
internal sealed class FileSystemExpander : IFileSystemExpander
{
    private readonly IVolumePathResolver _resolver;
    private readonly IVolumeAccess _volumes;
    private readonly DiskOperationRecorder _recorder;

    public FileSystemExpander(IVolumePathResolver resolver, IVolumeAccess volumes, DiskOperationRecorder recorder)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(volumes);
        ArgumentNullException.ThrowIfNull(recorder);

        _resolver = resolver;
        _volumes = volumes;
        _recorder = recorder;
    }

    /// <inheritdoc />
    public Task ExpandToPartitionAsync(PartitionGuid targetPartition, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        return Task.Run(() => Expand(targetPartition), ct);
    }

    /// <summary>
    /// The sector count the volume should have: the partition length divided by the sector size, minus one.
    /// NTFS keeps the last sector of the partition for the backup boot sector, which sits outside the volume (R2).
    /// </summary>
    internal static long TargetSectorCount(long partitionLengthBytes, long bytesPerSector)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerSector);

        return partitionLengthBytes / bytesPerSector - 1;
    }

    private void Expand(PartitionGuid partition)
    {
        using DiskOperationScope scope = _recorder.Begin(
            partition.Value.ToString("B", CultureInfo.InvariantCulture),
            "Extend");

        try
        {
            string volumeGuidPath = _resolver.GetVolumeGuidPath(partition);

            // The device path has no trailing backslash (\\?\Volume{...}).
            using IVolumeSession volume = _volumes.OpenReadWrite(volumeGuidPath.TrimEnd('\\'));

            long partitionLength = volume.GetLengthBytes();
            VolumeGeometry geometry = volume.GetNtfsGeometry();
            long targetSectors = TargetSectorCount(partitionLength, geometry.BytesPerSector);

            if (targetSectors > geometry.NumberSectors)
            {
                volume.ExtendToSectors(targetSectors);
            }

            scope.Succeed();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            scope.Fail(ex);
            throw new PlatformException(
                PlatformErrorCode.ExtendFailed,
                $"Could not extend the volume of partition {partition.Value.ToString("B", CultureInfo.InvariantCulture)}.",
                ex);
        }
    }
}
