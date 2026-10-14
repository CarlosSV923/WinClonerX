using WinCopy.Domain.Planning;

namespace WinCopy.Application.UseCases.Planning;

/// <summary>Confirma un plan en borrador tras aceptar sus avisos, escribir el sufijo del disco de destino y, si el plan
/// reduce el origen, autorizar esa reducción (FR-012 a FR-014).</summary>
public interface IConfirmClonePlan
{
    /// <summary>
    /// Relee los discos y confirma el plan. Si cambiaron, el plan se invalida y se lanza <see cref="PlanInvalidatedException"/>.
    /// Si el sufijo o los avisos no son válidos, lanza <see cref="InvalidOperationException"/> y el plan sigue en borrador.
    /// <paramref name="confirmSourceShrink"/> es la autorización explícita de reducir la partición de Windows del origen (FR-013):
    /// debe ser verdadera si el plan reduce el origen, y falsa si no lo reduce. Si no se cumple, el plan sigue en borrador.
    /// </summary>
    Task<ClonePlan> ExecuteAsync(
        PlanId id,
        IReadOnlyCollection<PreflightCode> acceptedWarnings,
        string typedSuffix,
        bool confirmSourceShrink,
        CancellationToken ct);
}
