using WinCopy.Domain.Planning;

namespace WinCopy.Application.UseCases.Planning;

/// <summary>Confirma un plan en borrador tras aceptar sus avisos y escribir el sufijo del disco de destino (FR-012 a FR-014).</summary>
public interface IConfirmClonePlan
{
    /// <summary>
    /// Relee los discos y confirma el plan. Si cambiaron, el plan se invalida y se lanza <see cref="PlanInvalidatedException"/>.
    /// Si el sufijo o los avisos no son válidos, lanza <see cref="InvalidOperationException"/> y el plan sigue en borrador.
    /// </summary>
    Task<ClonePlan> ExecuteAsync(
        PlanId id,
        IReadOnlyCollection<PreflightCode> acceptedWarnings,
        string typedSuffix,
        CancellationToken ct);
}
