using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>
/// Metadata reads needed to identify disks. The real implementation is <see cref="WindowsVolumeExtentsReader"/>.
/// Implementations must throw when they cannot answer, so that the caller fails closed.
/// </summary>
internal interface IVolumeExtentsReader
{
    /// <summary>Disk numbers that hold extents of the volume at <paramref name="volumePath"/>.</summary>
    IReadOnlySet<uint> DiskNumbersOf(string volumePath);

    /// <summary>Device path of the volume that holds the running Windows folder, for example <c>\\.\C:</c>.</summary>
    string WindowsVolumePath();

    /// <summary>
    /// Device path of the EFI system partition the machine booted from, read from
    /// <c>HKLM\SYSTEM\Setup\SystemPartition</c>. For example <c>\\?\GLOBALROOT\Device\HarddiskVolume1</c>.
    /// </summary>
    string SystemPartitionVolumePath();

    /// <summary>Bus type of the physical disk <paramref name="diskNumber"/>.</summary>
    STORAGE_BUS_TYPE BusTypeOf(int diskNumber);
}
