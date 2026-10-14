using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Storage;

namespace WinCopy.Infrastructure.Windows.Platform;

/// <summary>
/// Gathers the <see cref="PlatformFacts"/> that the preflight checks need: encryption of the source, battery, VSS,
/// free space on the Windows volume, and whether each non-system disk is in use.
/// </summary>
/// <remarks>
/// <para><b>TargetInUse</b> is true for a non-system disk when any of its volumes cannot be locked exclusively. The system
/// disk is never probed. A disk with no volumes is not in use. A probe that throws counts as in use (fail-safe). A
/// missing key in the dictionary is treated as in use by the evaluator as well.</para>
/// <para><b>MinimumWindowsSize</b> is null. It needs <c>MSFT_Partition.GetSupportedSize</c>, which is US2 (TODO(US2), T092).</para>
/// </remarks>
internal sealed class PlatformFactsProvider : IPlatformFactsProvider
{
    internal const string VssServiceName = "VSS";

    private readonly IPowerManager _power;
    private readonly IServiceCatalog _services;
    private readonly IVolumePathResolver _volumePaths;
    private readonly IVolumeLockProbe _lockProbe;

    public PlatformFactsProvider(
        IPowerManager power,
        IServiceCatalog services,
        IVolumePathResolver volumePaths,
        IVolumeLockProbe lockProbe)
    {
        ArgumentNullException.ThrowIfNull(power);
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(volumePaths);
        ArgumentNullException.ThrowIfNull(lockProbe);

        _power = power;
        _services = services;
        _volumePaths = volumePaths;
        _lockProbe = lockProbe;
    }

    /// <inheritdoc />
    public async Task<PlatformFacts> GetAsync(DiskInventory inventory, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ct.ThrowIfCancellationRequested();

        PowerStatus power = await _power.GetStatusAsync(ct);

        Volume? windowsVolume = FindWindowsVolume(inventory);
        bool snapshotAvailable = windowsVolume?.FileSystem == FileSystemKind.Ntfs && IsVssEnabled();
        ByteSize snapshotFreeSpace = windowsVolume is null
            ? new ByteSize(0)
            : windowsVolume.Capacity - windowsVolume.Used;

        IReadOnlyDictionary<DiskId, bool> targetInUse = await Task.Run(() => ProbeTargets(inventory, ct), ct);

        return new PlatformFacts(
            SourceEncryption(inventory),
            power.OnBattery,
            power.BatteryPercent,
            snapshotAvailable,
            snapshotFreeSpace,

            // TODO(US2): MSFT_Partition.GetSupportedSize (T092). Until then the shrink check has no minimum.
            MinimumWindowsSize: null,
            targetInUse);
    }

    /// <summary>Encryption of each volume on the source (system) disk, keyed by partition GUID.</summary>
    private static Dictionary<PartitionGuid, EncryptionState> SourceEncryption(DiskInventory inventory)
    {
        var states = new Dictionary<PartitionGuid, EncryptionState>();

        foreach (Partition partition in inventory.SystemDisk?.Partitions ?? [])
        {
            if (partition.Volume is not null)
            {
                states[partition.UniqueId] = partition.Volume.Encryption;
            }
        }

        return states;
    }

    private static Volume? FindWindowsVolume(DiskInventory inventory) =>
        inventory.Disks
            .SelectMany(disk => disk.Partitions)
            .Select(partition => partition.Volume)
            .FirstOrDefault(volume => volume is { IsWindowsVolume: true });

    /// <summary>VSS counts as available when the service is installed and not disabled. Any failure means unavailable.</summary>
    private bool IsVssEnabled()
    {
        try
        {
            ServiceStartKind? kind = _services.GetStartKind(VssServiceName);
            return kind is not null && kind != ServiceStartKind.Disabled;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // It cannot be told whether VSS is there. Report no snapshot support, which the evaluator blocks.
            return false;
        }
    }

    /// <summary>Probes every non-system disk. A disk is in use when any of its volumes cannot be locked.</summary>
    private Dictionary<DiskId, bool> ProbeTargets(DiskInventory inventory, CancellationToken ct)
    {
        var inUse = new Dictionary<DiskId, bool>();

        foreach (Disk disk in inventory.Disks)
        {
            // The system disk is never probed (FR-004). Its volumes are not candidates.
            if (disk.IsSystem)
            {
                continue;
            }

            ct.ThrowIfCancellationRequested();

            bool diskInUse = false;
            foreach (Partition partition in disk.Partitions)
            {
                if (partition.Volume is not null && IsInUse(partition))
                {
                    diskInUse = true;
                    break;
                }
            }

            // DiskId falls back to the disk number for non-GPT disks, so keys are distinct. The merge stays as a
            // defensive fail-safe: if a key ever repeated, "in use" would win.
            bool previouslyInUse = inUse.TryGetValue(disk.Id, out bool previous) && previous;
            inUse[disk.Id] = previouslyInUse || diskInUse;
        }

        return inUse;
    }

    private bool IsInUse(Partition partition)
    {
        try
        {
            string volumeGuidPath = _volumePaths.GetVolumeGuidPath(partition.UniqueId);
            return !_lockProbe.CanLockExclusively(VolumePath.ToDevicePath(volumeGuidPath));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A volume that cannot be probed is treated as in use.
            return true;
        }
    }
}
