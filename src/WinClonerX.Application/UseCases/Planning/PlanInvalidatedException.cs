using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.UseCases.Planning;

/// <summary>
/// Se lanza al confirmar un plan cuyos discos cambiaron desde que se calculó (FR-011). El plan queda invalidado
/// y hay que volver a planificar.
/// </summary>
public sealed class PlanInvalidatedException(PlanId planId, string reason)
    : Exception("El plan de clonado ya no es válido porque cambió el estado de los discos: " + reason + ".")
{
    /// <summary>Plan que se ha invalidado.</summary>
    public PlanId PlanId { get; } = planId;

    /// <summary>Motivo técnico de la invalidación, por ejemplo "DisksChanged".</summary>
    public string Reason { get; } = reason;
}
