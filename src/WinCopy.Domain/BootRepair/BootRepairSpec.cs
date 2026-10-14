using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.BootRepair;

/// <summary>
/// Ajustes que el clon necesita para arrancar, derivados de un plan: las particiones nuevas de Windows, EFI y recuperación,
/// la reescritura de MountedDevices y los pasos a ejecutar. Es un objeto de valor, sin acceso a disco.
/// </summary>
public sealed record BootRepairSpec(
    PartitionGuid WindowsPartition,
    PartitionGuid EspPartition,
    PartitionGuid? RecoveryPartition,
    MountedDevicesRewrite MountedDevices,
    IReadOnlyList<BootRepairStep> Steps)
{
    /// <summary>Letra que el clon debe usar para su volumen de Windows.</summary>
    public const char WindowsDriveLetter = 'C';

    /// <summary>
    /// Deriva la especificación del plan. Usa los GUID nuevos de las particiones de Windows, EFI y recuperación. Los valores de
    /// MountedDevices a eliminar son los de todas las particiones del origen. Lanza <see cref="InvalidOperationException"/> si el
    /// plan no tiene exactamente una partición de Windows ni exactamente una EFI.
    /// </summary>
    public static BootRepairSpec From(ClonePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var windows = RequireSingle(plan, PartitionRole.Windows, "Windows");
        var esp = RequireSingle(plan, PartitionRole.EfiSystem, "EFI");
        var recovery = FindOptional(plan, PartitionRole.Recovery, "recuperación");

        var steps = new List<BootRepairStep>
        {
            BootRepairStep.RewriteMountedDevices,
            BootRepairStep.RebuildBcd,
        };
        if (recovery is not null)
        {
            steps.Add(BootRepairStep.ReconfigureWinRe);
        }

        var staleValues = plan.Mappings
            .Select(mapping => MountedDevicesRewrite.GptValue(mapping.Source.UniqueId.Value))
            .ToArray();
        var mountedDevices = new MountedDevicesRewrite(
            WindowsDriveLetter,
            MountedDevicesRewrite.GptValue(windows.Value),
            staleValues);

        return new BootRepairSpec(windows, esp, recovery, mountedDevices, steps);
    }

    private static PartitionGuid RequireSingle(ClonePlan plan, PartitionRole role, string name) =>
        FindOptional(plan, role, name)
        ?? throw new InvalidOperationException($"El plan no tiene una partición de {name}.");

    private static PartitionGuid? FindOptional(ClonePlan plan, PartitionRole role, string name)
    {
        var matches = plan.Mappings.Where(mapping => mapping.Source.Role == role).ToArray();
        if (matches.Length > 1)
        {
            throw new InvalidOperationException($"El plan tiene más de una partición de {name}.");
        }

        return matches.Length == 1 ? matches[0].NewUniqueId : null;
    }
}
