using System.Globalization;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Evalúa las comprobaciones previas del origen (global) y la elegibilidad de cada disco distinto del sistema
/// como destino (FR-003 a FR-010). No accede al disco: todo llega en el inventario y en los hechos.
/// El disco del sistema nunca aparece entre los destinos (FR-004).
/// </summary>
public static class PreflightEvaluator
{
    private const long MinimumSnapshotFreeSpaceBytes = 2L * 1024 * 1024 * 1024;

    /// <summary>
    /// Devuelve el informe global y, para cada disco distinto del sistema, su elegibilidad.
    /// Un valor de <see cref="PlatformFacts.TargetInUse"/> ausente se trata como "en uso" (no comprobado bloquea).
    /// </summary>
    public static (PreflightReport Global, IReadOnlyList<TargetEligibility> Targets) Evaluate(
        DiskInventory inventory,
        PlatformFacts facts,
        CapacityPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(policy);

        var global = new List<PreflightCheck>
        {
            inventory.Firmware == FirmwareType.Uefi ? Pass(PreflightCode.FirmwareNotUefi) : Block(PreflightCode.FirmwareNotUefi),
        };

        if (!inventory.IsSupported)
        {
            global.Add(Block(PreflightCode.UnsupportedDiskKind, Data(("Reason", inventory.UnsupportedReason ?? "Unknown"))));
            return (new PreflightReport(global), Array.Empty<TargetEligibility>());
        }

        var source = inventory.SystemDisk!;
        var windowsIndex = WindowsPartitionIndex(source);

        global.Add(source.PartitionStyle == PartitionStyle.Gpt ? Pass(PreflightCode.SourceNotGpt) : Block(PreflightCode.SourceNotGpt));
        global.Add(SourceKindCheck(source));
        global.Add(MultipleWindowsCheck(source));
        global.Add(facts.SnapshotAvailable ? Pass(PreflightCode.SnapshotUnavailable) : Block(PreflightCode.SnapshotUnavailable));
        global.Add(facts.SnapshotFreeSpace.Bytes >= MinimumSnapshotFreeSpaceBytes
            ? Pass(PreflightCode.SnapshotSpaceLow)
            : Block(PreflightCode.SnapshotSpaceLow, Data(("FreeSpace", facts.SnapshotFreeSpace))));
        global.Add(BitLockerCheck(source, facts));
        global.Add(OnBatteryCheck(facts));

        var globalReport = new PreflightReport(global);
        var targets = inventory.Disks
            .Where(disk => !disk.IsSystem)
            .Select(disk => EvaluateTarget(source, windowsIndex, disk, facts, policy, globalReport.IsBlocked))
            .ToArray();

        return (globalReport, targets);
    }

    private static TargetEligibility EvaluateTarget(
        Disk source,
        int windowsIndex,
        Disk target,
        PlatformFacts facts,
        CapacityPolicy policy,
        bool globalBlocked)
    {
        var reasons = new List<PreflightCheck>
        {
            target.LogicalSectorSize == source.LogicalSectorSize
                ? Pass(PreflightCode.SectorSizeMismatch)
                : Block(PreflightCode.SectorSizeMismatch),
            IsInUse(target, facts) ? Block(PreflightCode.TargetInUse) : Pass(PreflightCode.TargetInUse),
        };

        var (sizeCheck, strategy, shortfall) = CheckSize(source, windowsIndex, target, policy);
        reasons.Add(sizeCheck);
        reasons.Add(target.Partitions.Count > 0 ? Warn(PreflightCode.TargetNotEmpty) : Pass(PreflightCode.TargetNotEmpty));

        var causes = reasons.Where(check => check.Severity != Severity.Pass).ToArray();
        var isEligible = !globalBlocked && causes.All(check => check.Severity != Severity.Blocking);

        return new TargetEligibility(target.Id, isEligible, causes, strategy, shortfall);
    }

    private static (PreflightCheck Check, SizeStrategy? Strategy, ByteSize? Shortfall) CheckSize(
        Disk source,
        int windowsIndex,
        Disk target,
        CapacityPolicy policy)
    {
        // Sin una única partición de Windows en el origen no hay tamaño que comparar; la comprobación global ya bloquea.
        if (windowsIndex < 0)
        {
            return (Pass(PreflightCode.TargetTooSmall), null, null);
        }

        // TODO(US2): cuando el destino sea más pequeño, evaluar la estrategia ShrinkSource y PreflightCode.ShrinkInsufficient.
        var layout = TargetLayoutCalculator.Calculate(source, target);
        if (!layout.IsSuccess)
        {
            if (layout.Error.Code == TargetLayoutCalculator.TargetTooSmallCode)
            {
                var missing = new ByteSize(long.Parse(layout.Error.Detail!, CultureInfo.InvariantCulture));
                return (Block(PreflightCode.TargetTooSmall, Data(("Shortfall", missing))), null, missing);
            }

            return (Block(PreflightCode.UnsupportedDiskKind, Data(("Reason", layout.Error.Code))), null, null);
        }

        // La partición de Windows debe conservar sus datos ocupados más el margen (FR-010).
        var windowsUsed = source.Partitions[windowsIndex].Volume!.Used;
        var required = policy.Required(windowsUsed);
        var targetWindowsLength = layout.Value[windowsIndex].Length;
        if (targetWindowsLength < required.Bytes)
        {
            var missing = required - new ByteSize(targetWindowsLength);
            return (Block(PreflightCode.TargetTooSmall, Data(("Shortfall", missing))), null, missing);
        }

        return (Pass(PreflightCode.TargetTooSmall), SizeStrategy.SameOrLarger, null);
    }

    private static PreflightCheck SourceKindCheck(Disk source)
    {
        if (source.Bus is BusKind.Spaces or BusKind.Raid)
        {
            return Block(PreflightCode.UnsupportedDiskKind, Data(("Reason", "UnsupportedBus")));
        }

        return source.Partitions.Any(partition => partition.Role == PartitionRole.Unsupported)
            ? Block(PreflightCode.UnsupportedDiskKind, Data(("Reason", "UnsupportedPartition")))
            : Pass(PreflightCode.UnsupportedDiskKind);
    }

    private static PreflightCheck MultipleWindowsCheck(Disk source)
    {
        var windowsPartitions = source.Partitions.Count(partition => partition.Role == PartitionRole.Windows);
        var windowsVolumes = source.Partitions.Count(partition => partition.Volume?.IsWindowsVolume == true);

        return windowsPartitions == 1 && windowsVolumes == 1
            ? Pass(PreflightCode.MultipleWindows)
            : Block(PreflightCode.MultipleWindows, Data(("WindowsPartitions", windowsPartitions), ("WindowsVolumes", windowsVolumes)));
    }

    private static PreflightCheck BitLockerCheck(Disk source, PlatformFacts facts)
    {
        var encrypted = source.Partitions.Count(partition => IsBitLockerProtected(partition, facts));

        return encrypted > 0
            ? Warn(PreflightCode.BitLockerOn, Data(("Volumes", encrypted)))
            : Pass(PreflightCode.BitLockerOn);
    }

    private static PreflightCheck OnBatteryCheck(PlatformFacts facts)
    {
        if (!facts.OnBattery)
        {
            return Pass(PreflightCode.OnBattery);
        }

        return facts.BatteryPercent is { } percent
            ? Warn(PreflightCode.OnBattery, Data(("BatteryPercent", percent)))
            : Warn(PreflightCode.OnBattery);
    }

    // Un volumen con BitLocker suspendido también se clona sin cifrar: cuenta igual que uno activo.
    private static bool IsBitLockerProtected(Partition partition, PlatformFacts facts) =>
        IsBitLockerState(partition.Volume?.Encryption)
        || (facts.Encryption.TryGetValue(partition.UniqueId, out var state) && IsBitLockerState(state));

    private static bool IsBitLockerState(EncryptionState? state) =>
        state is EncryptionState.BitLockerOn or EncryptionState.BitLockerSuspended;

    private static bool IsInUse(Disk target, PlatformFacts facts) =>
        !facts.TargetInUse.TryGetValue(target.Id, out var inUse)
        || inUse
        || target.IsReadOnly
        || target.Partitions.Any(partition => partition.Volume?.InUseByOthers == true);

    private static int WindowsPartitionIndex(Disk source)
    {
        var indexes = Enumerable.Range(0, source.Partitions.Count)
            .Where(i => source.Partitions[i].Role == PartitionRole.Windows)
            .ToArray();

        return indexes.Length == 1 && source.Partitions[indexes[0]].Volume is not null ? indexes[0] : -1;
    }

    private static PreflightCheck Pass(PreflightCode code) => Check(code, Severity.Pass, null);

    private static PreflightCheck Warn(PreflightCode code, IReadOnlyDictionary<string, object>? facts = null) =>
        Check(code, Severity.Warning, facts);

    private static PreflightCheck Block(PreflightCode code, IReadOnlyDictionary<string, object>? facts = null) =>
        Check(code, Severity.Blocking, facts);

    private static PreflightCheck Check(PreflightCode code, Severity severity, IReadOnlyDictionary<string, object>? facts) =>
        new(code, severity, facts ?? PreflightCheck.EmptyFacts());

    private static Dictionary<string, object> Data(params (string Key, object Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
}
