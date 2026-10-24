using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Execution;

/// <summary>Planes ya confirmados para las pruebas de ejecución y de reparación de arranque.</summary>
internal static class ConfirmedPlanFixtures
{
    /// <summary>
    /// Plan confirmado con el disco del sistema típico y un destino del tamaño indicado. Con 256 GiB (por defecto) el destino
    /// tiene el mismo tamaño que el origen; con más, la partición de Windows crece y necesita ampliar el sistema de archivos.
    /// </summary>
    public static ClonePlan Confirmed(
        long targetSize = 256 * Gib,
        VerificationMode mode = VerificationMode.Structural,
        Disk? systemDisk = null)
    {
        var target = TargetDisk(size: targetSize);
        var inventory = InventoryOf(systemDisk ?? SystemDisk(), target);
        var plan = PlanFor(inventory, mode: mode).Value;

        plan.Confirm(new TargetConfirmation(target.Id, TargetConfirmation.ExpectedSuffix(target), CapturedAt), target);

        return plan;
    }
}
