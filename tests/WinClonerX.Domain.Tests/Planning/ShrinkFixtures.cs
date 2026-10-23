using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using static WinClonerX.Domain.Tests.Planning.PlanningFixtures;

namespace WinClonerX.Domain.Tests.Planning;

/// <summary>
/// Planes con reducción del origen para las pruebas. Con el disco típico (origen de 256 GiB, Windows de 256 GiB menos 868 MiB
/// y 40 GiB ocupados, margen de 10 GiB), un destino de 200 GiB no cabe sin reducir.
/// </summary>
internal static class ShrinkFixtures
{
    /// <summary>Destino que no admite el diseño igual o mayor: 200 GiB frente a 256 GiB.</summary>
    public const long SmallTargetSize = 200 * Gib;

    /// <summary>Espacio que Windows conserva en el destino de <see cref="SmallTargetSize"/>: 200 GiB menos 868 MiB.</summary>
    public const long SmallTargetWindowsLength = 200 * Gib - 868 * Mib;

    /// <summary>Longitud de la partición de Windows del origen típico: 256 GiB menos 868 MiB.</summary>
    public const long SourceWindowsLength = 256 * Gib - 868 * Mib;

    /// <summary>Hechos por defecto con el mínimo de Windows indicado.</summary>
    public static PlatformFacts FactsWithMinimum(ByteSize? minimum) => Facts() with { MinimumWindowsSize = minimum };

    /// <summary>Plan sin confirmar de reducción para el destino indicado, con su disco de destino.</summary>
    public static (ClonePlan Plan, Disk Target) ShrinkPlan(
        ByteSize? minimum,
        long targetSize = SmallTargetSize,
        long windowsUsed = 40 * Gib,
        CapacityPolicy? policy = null,
        VerificationMode mode = VerificationMode.Structural)
    {
        var target = TargetDisk(size: targetSize, serial: "SN-SHRINK01");
        var inventory = InventoryOf(SystemDisk(windowsUsed: windowsUsed), target);
        var plan = PlanFor(inventory, facts: FactsWithMinimum(minimum), policy: policy, mode: mode).Value;

        return (plan, target);
    }

    /// <summary>Confirmación de la reducción que corresponde al plan.</summary>
    public static ShrinkConfirmation ConfirmationOf(ClonePlan plan)
    {
        var shrink = plan.SourceShrink ?? throw new InvalidOperationException("El plan no reduce el origen.");
        return new ShrinkConfirmation(shrink.Partition, shrink.ShrunkLength, CapturedAt);
    }

    /// <summary>Plan de reducción ya confirmado, con ambas confirmaciones.</summary>
    public static ClonePlan ConfirmedShrinkPlan(
        ByteSize? minimum,
        long targetSize = SmallTargetSize,
        VerificationMode mode = VerificationMode.Structural)
    {
        var (plan, target) = ShrinkPlan(minimum, targetSize, mode: mode);
        plan.Confirm(new TargetConfirmation(target.Id, TargetConfirmation.ExpectedSuffix(target), CapturedAt), target, ConfirmationOf(plan));

        return plan;
    }
}
