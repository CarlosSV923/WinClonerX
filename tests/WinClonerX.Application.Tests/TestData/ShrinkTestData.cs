using WinClonerX.Application.Tests.Fakes;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using static WinClonerX.Application.Tests.TestData.PlanningTestData;

namespace WinClonerX.Application.Tests.TestData;

/// <summary>
/// Planes con reducción del origen (US2) para las pruebas de aplicación, construidos con <see cref="ClonePlanner"/>.
/// Con el disco típico de <see cref="PlanningTestData"/> (origen de 256 GiB, Windows de 256 GiB menos 868 MiB y 40 GiB
/// ocupados) y un destino de 200 GiB, Windows no cabe sin reducir: el plan reduce la partición de Windows a su longitud
/// reducida y la amplía después en el destino. Copia adaptada de <c>ShrinkFixtures</c> del dominio; la capa de
/// aplicación no referencia el proyecto de pruebas del dominio.
/// </summary>
internal static class ShrinkTestData
{
    /// <summary>Sufijo que hay que escribir para confirmar <see cref="ShrinkTarget"/>: los 4 últimos caracteres de "TGT-0002".</summary>
    public const string ShrinkTargetSuffix = "0002";

    /// <summary>Partición de Windows del origen típico, la única que puede reducirse.</summary>
    public static readonly PartitionGuid WindowsPartition = SourcePartitionGuid(3);

    /// <summary>Mínimo que admite Windows en los planes de prueba, y el que devuelve el fake por defecto.</summary>
    public static readonly ByteSize DefaultMinimum = ByteSize.FromGiB(10);

    /// <summary>Destino de 200 GiB: más pequeño que la partición de Windows del origen.</summary>
    public static Disk ShrinkTarget() => TargetDisk(size: 200 * Gib);

    /// <summary>Inventario con el origen típico y el destino de 200 GiB.</summary>
    public static DiskInventory ShrinkInventory() => InventoryOf(SystemDisk(), ShrinkTarget());

    /// <summary>Inventario con el origen típico y el destino de 512 GiB: no requiere reducción.</summary>
    public static DiskInventory NonShrinkInventory() => InventoryOf(SystemDisk(), TargetDisk());

    /// <summary>Hechos por defecto con el mínimo de Windows indicado.</summary>
    public static PlatformFacts ShrinkFacts(ByteSize minimum) => Facts() with { MinimumWindowsSize = minimum };

    /// <summary>Plan en borrador que reduce la partición de Windows para entrar en <see cref="ShrinkTarget"/>.</summary>
    public static ClonePlan ShrinkDraft(ByteSize? minimum = null)
    {
        var result = ClonePlanner.Plan(
            ShrinkInventory(),
            TargetId,
            ShrinkFacts(minimum ?? DefaultMinimum),
            VerificationMode.Structural,
            CapacityPolicy.Default,
            SequentialGuids());

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("El plan de reducción de prueba no es válido; revisar los datos de prueba.");
    }

    /// <summary>Plan en borrador que no reduce el origen (destino de 512 GiB).</summary>
    public static ClonePlan NonShrinkDraft()
    {
        var result = ClonePlanner.Plan(
            NonShrinkInventory(),
            TargetId,
            Facts(),
            VerificationMode.Structural,
            CapacityPolicy.Default,
            SequentialGuids());

        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("El plan sin reducción de prueba no es válido; revisar los datos de prueba.");
    }

    /// <summary>Confirmación de la reducción que corresponde al plan: la partición y la longitud reducida que la persona ha visto.</summary>
    public static ShrinkConfirmation ConfirmationOf(ClonePlan plan)
    {
        var shrink = plan.SourceShrink ?? throw new InvalidOperationException("El plan no reduce el origen.");
        return new ShrinkConfirmation(shrink.Partition, shrink.ShrunkLength, FakeDefaults.Now);
    }

    /// <summary>Plan de reducción ya confirmado, con el destino y la reducción confirmados.</summary>
    public static ClonePlan ConfirmedShrinkPlan(ByteSize? minimum = null)
    {
        var plan = ShrinkDraft(minimum);
        plan.AcceptWarnings(plan.Warnings.Select(warning => warning.Code).ToArray());
        plan.Confirm(new TargetConfirmation(TargetId, ShrinkTargetSuffix, FakeDefaults.Now), ShrinkTarget(), ConfirmationOf(plan));

        return plan;
    }
}
