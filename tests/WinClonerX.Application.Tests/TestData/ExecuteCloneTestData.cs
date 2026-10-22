using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Application.Tests.Fakes;
using static WinClonerX.Application.Tests.TestData.PlanningTestData;

namespace WinClonerX.Application.Tests.TestData;

/// <summary>
/// Planes de prueba para el orquestador ExecuteClone, construidos con <see cref="ClonePlanner"/> sobre el disco típico
/// de <see cref="PlanningTestData"/>. Con un destino de 512 GiB, la partición de Windows crece y se planifica ExpandFileSystem.
/// </summary>
internal static class ExecuteCloneTestData
{
    /// <summary>Tamaño de fragmento que usa <see cref="FakeVolumeCopier"/>.</summary>
    public const int ChunkSize = 4096;

    /// <summary>Sufijo de confirmación del disco de destino típico (últimos 4 caracteres de "TGT-0002").</summary>
    public const string TargetSuffix = "0002";

    /// <summary>Partición NTFS de Windows del disco de origen típico.</summary>
    public static readonly PartitionGuid WindowsSource = SourcePartitionGuid(3);

    /// <summary>Partición NTFS de recuperación del disco de origen típico.</summary>
    public static readonly PartitionGuid RecoverySource = SourcePartitionGuid(4);

    /// <summary>Inventario estándar: origen típico y destino de 512 GiB sin particiones. Coincide con los planes de prueba.</summary>
    public static DiskInventory StandardInventory() => InventoryOf(SystemDisk(), TargetDisk());

    /// <summary>Plan en borrador, sin confirmar, calculado con el inventario estándar.</summary>
    public static ClonePlan DraftPlan(PlatformFacts? facts = null, VerificationMode mode = VerificationMode.Structural)
    {
        var result = ClonePlanner.Plan(StandardInventory(), TargetId, facts ?? Facts(), mode, CapacityPolicy.Default, SequentialGuids());
        return result.IsSuccess
            ? result.Value
            : throw new InvalidOperationException("El plan de prueba no es válido; revisar los datos de prueba.");
    }

    /// <summary>Plan confirmado: acepta todos sus avisos y se confirma con el sufijo del disco de destino.</summary>
    public static ClonePlan ConfirmedPlan(PlatformFacts? facts = null, VerificationMode mode = VerificationMode.Structural)
    {
        var plan = DraftPlan(facts, mode);
        plan.AcceptWarnings(plan.Warnings.Select(warning => warning.Code).ToArray());
        plan.Confirm(new TargetConfirmation(TargetId, TargetSuffix, FakeDefaults.Now), TargetDisk());

        return plan;
    }

    /// <summary>Hechos con BitLocker activo en la partición de Windows del origen (genera el aviso BitLockerOn).</summary>
    public static PlatformFacts BitLockerFacts() => Facts() with
    {
        Encryption = new Dictionary<PartitionGuid, EncryptionState> { [WindowsSource] = EncryptionState.BitLockerOn },
    };
}
