using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.UseCases.Planning;

/// <summary>Calcula el plan de clonado a un disco de destino a partir de la última captura (FR-007 a FR-010).</summary>
public interface IBuildClonePlan
{
    /// <summary>
    /// Devuelve el plan en borrador y lo guarda, o el informe de comprobaciones si el destino no es apto.
    /// Lanza <see cref="InvalidOperationException"/> si no hay un inventario capturado.
    /// </summary>
    Task<Result<ClonePlan, PreflightReport>> ExecuteAsync(DiskId target, VerificationMode mode, CancellationToken ct);
}
