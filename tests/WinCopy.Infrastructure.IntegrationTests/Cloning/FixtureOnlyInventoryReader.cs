using WinCopy.Application.Ports;
using WinCopy.Domain.Inventory;
using WinCopy.Infrastructure.IntegrationTests.Fixtures;

namespace WinCopy.Infrastructure.IntegrationTests.Cloning;

/// <summary>
/// Test-only decorator over the real <see cref="IDiskInventoryReader"/>. The inventory it returns contains only the disks that
/// a <see cref="VirtualDiskFixture"/> of this process attached (<see cref="VirtualDiskRegistry"/>). The reference source disk is
/// marked as the system disk, and its "Windows" partition's volume is marked as the Windows volume, because the reference
/// disk is not the running Windows installation. Every other kept disk is marked as not system.
/// </summary>
/// <remarks>
/// The real system disk never appears here. If a kept disk is one that the real reader marked as system, the read throws
/// instead of returning a set that could let the planner pick the running system.
/// </remarks>
internal sealed class FixtureOnlyInventoryReader(IDiskInventoryReader inner, int sourceDiskNumber, Guid windowsPartitionGuid)
    : IDiskInventoryReader
{
    /// <inheritdoc />
    public async Task<DiskInventory> ReadAsync(CancellationToken ct)
    {
        DiskInventory real = await inner.ReadAsync(ct);

        var kept = new List<Disk>();
        foreach (Disk disk in real.Disks)
        {
            if (!VirtualDiskRegistry.Contains(disk.Id.Number))
            {
                continue;
            }

            if (disk.IsSystem)
            {
                throw new InvalidOperationException(
                    $"Refusing to build the test inventory: disk {disk.Id.Number} is a fixture disk but the real reader marked it as the system disk.");
            }

            kept.Add(Rebuild(disk, isSource: disk.Id.Number == sourceDiskNumber));
        }

        if (!kept.Any(disk => disk.Id.Number == sourceDiskNumber))
        {
            throw new InvalidOperationException($"The reference source disk {sourceDiskNumber} is not in the real inventory.");
        }

        return new DiskInventory(real.CapturedAt, real.Firmware, kept);
    }

    private Disk Rebuild(Disk disk, bool isSource)
    {
        var partitions = new List<Partition>(disk.Partitions.Count);
        foreach (Partition partition in disk.Partitions)
        {
            Volume? volume = partition.Volume;
            if (isSource && partition.UniqueId.Value == windowsPartitionGuid)
            {
                if (volume is null)
                {
                    throw new InvalidOperationException("The reference Windows partition has no volume in the real inventory.");
                }

                volume = new Volume(
                    volume.FileSystem,
                    volume.Capacity,
                    volume.Used,
                    volume.ClusterSize,
                    volume.Encryption,
                    IsWindowsVolume: true,
                    volume.InUseByOthers);
            }

            partitions.Add(ReferenceEquals(volume, partition.Volume)
                ? partition
                : new Partition(
                    partition.Number,
                    partition.Extent,
                    partition.Type,
                    partition.UniqueId,
                    partition.Attributes,
                    partition.Name,
                    volume));
        }

        return new Disk(
            disk.Id,
            disk.Model,
            disk.SerialNumber,
            disk.Size,
            disk.LogicalSectorSize,
            disk.PhysicalSectorSize,
            disk.Bus,
            disk.PartitionStyle,
            isSource,
            partitions,
            disk.IsOffline,
            disk.IsReadOnly);
    }
}
