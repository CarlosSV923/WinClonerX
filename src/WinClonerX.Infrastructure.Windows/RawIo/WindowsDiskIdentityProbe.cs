using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.RawIo;

/// <summary>
/// Builds the identity of a physical disk from metadata reads only. Any failure throws, and
/// <see cref="DiskWriteGuard"/> then denies the write.
/// </summary>
/// <remarks>
/// Identity rule:
/// <list type="bullet">
/// <item><see cref="DiskWriteCandidate.ContainsRunningWindows"/> is true when the disk holds an extent of the volume
/// that contains the running Windows folder.</item>
/// <item><see cref="DiskWriteCandidate.IsSystemDisk"/> is true when the disk holds an extent of the EFI system
/// partition the machine booted from (the volume named by <c>HKLM\SYSTEM\Setup\SystemPartition</c>), or when
/// <see cref="DiskWriteCandidate.ContainsRunningWindows"/> is true. A disk that holds only the EFI system partition
/// is therefore a system disk, even though Windows is not on it.</item>
/// </list>
/// </remarks>
internal sealed class WindowsDiskIdentityProbe : IDiskIdentityProbe
{
    private readonly IVolumeExtentsReader _reader;

    public WindowsDiskIdentityProbe()
        : this(new WindowsVolumeExtentsReader())
    {
    }

    public WindowsDiskIdentityProbe(IVolumeExtentsReader reader)
    {
        ArgumentNullException.ThrowIfNull(reader);

        _reader = reader;
    }

    public DiskWriteCandidate Probe(int diskNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

        STORAGE_BUS_TYPE busType = _reader.BusTypeOf(diskNumber);

        IReadOnlySet<uint> windowsDisks = _reader.DiskNumbersOf(_reader.WindowsVolumePath());
        IReadOnlySet<uint> systemPartitionDisks = _reader.DiskNumbersOf(_reader.SystemPartitionVolumePath());

        uint number = (uint)diskNumber;
        bool containsRunningWindows = windowsDisks.Contains(number);
        bool hostsSystemPartition = systemPartitionDisks.Contains(number);

        return new DiskWriteCandidate(
            diskNumber,
            busType,
            IsSystemDisk: hostsSystemPartition || containsRunningWindows,
            ContainsRunningWindows: containsRunningWindows);
    }
}
