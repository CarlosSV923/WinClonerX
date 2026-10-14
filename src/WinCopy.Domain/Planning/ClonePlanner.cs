using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Planificador de clonado (servicio de dominio). Evalúa las comprobaciones previas, rechaza el destino si hay algún
/// bloqueo y, si no, construye el plan en borrador con las correspondencias de partición y los GUID nuevos.
/// </summary>
public static class ClonePlanner
{
    /// <summary>
    /// Planifica el clonado del disco del sistema al disco <paramref name="target"/>. Si hay algún bloqueo, devuelve el
    /// informe completo como error. <paramref name="newGuid"/> genera, en este orden, el identificador del plan, el GUID
    /// de disco y un GUID por partición; se inyecta para que las pruebas sean deterministas.
    /// </summary>
    public static Result<ClonePlan, PreflightReport> Plan(
        DiskInventory inventory,
        DiskId target,
        PlatformFacts facts,
        VerificationMode mode,
        CapacityPolicy policy,
        Func<Guid> newGuid)
    {
        ArgumentNullException.ThrowIfNull(inventory);
        ArgumentNullException.ThrowIfNull(facts);
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(newGuid);

        var targetDisk = inventory.FindDisk(target)
            ?? throw new ArgumentException("El disco de destino no está en el inventario.", nameof(target));

        var (global, targets) = PreflightEvaluator.Evaluate(inventory, facts, policy);

        if (targetDisk.IsSystem)
        {
            var systemTarget = new PreflightCheck(PreflightCode.TargetIsSystem, Severity.Blocking, PreflightCheck.EmptyFacts());
            return Result<ClonePlan, PreflightReport>.Failure(new PreflightReport([.. global.Checks, systemTarget]));
        }

        var eligibility = targets.FirstOrDefault(candidate => candidate.Disk == target);
        if (global.IsBlocked || eligibility is null || !eligibility.IsEligible)
        {
            var reasons = eligibility?.Reasons ?? Array.Empty<PreflightCheck>();
            return Result<ClonePlan, PreflightReport>.Failure(new PreflightReport([.. global.Checks, .. reasons]));
        }

        var source = inventory.SystemDisk
            ?? throw new InvalidOperationException("El inventario no tiene un disco del sistema.");
        var layout = TargetLayoutCalculator.Calculate(source, targetDisk);
        if (!layout.IsSuccess)
        {
            throw new InvalidOperationException("La elegibilidad indica un destino apto, pero el diseño no se puede calcular.");
        }

        var planId = new PlanId(newGuid());
        var newDiskGuid = newGuid();
        var extents = layout.Value;
        var mappings = source.Partitions
            .Select((partition, i) => new PartitionMapping(
                partition,
                extents[i],
                new PartitionGuid(newGuid()),
                CopyMethodFor(partition),
                ResizeFor(partition, extents[i])))
            .ToArray();

        var warnings = global.Warnings
            .Concat(eligibility.Reasons.Where(reason => reason.Severity == Severity.Warning))
            .ToArray();

        var plan = ClonePlan.Create(
            planId,
            source,
            targetDisk,
            inventory.Fingerprint(source.Id, target),
            mappings,
            newDiskGuid,
            mode,
            warnings,
            CloneEstimate.Calculate(mappings, source.Bus, targetDisk.Bus),
            policy);

        return Result<ClonePlan, PreflightReport>.Success(plan);
    }

    private static CopyMethod CopyMethodFor(Partition partition) =>
        partition.Volume?.FileSystem == FileSystemKind.Ntfs ? CopyMethod.UsedClustersFromSnapshot : CopyMethod.RawFull;

    private static ResizeKind ResizeFor(Partition partition, Extent targetExtent) =>
        partition.Role == PartitionRole.Windows && targetExtent.Length > partition.Extent.Length
            ? ResizeKind.GrowFileSystem
            : ResizeKind.None;
}
