using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;

namespace WinCopy.Application.Ports;

/// <summary>Verificación estructural del clon ya escrito: tabla de particiones igual al plan, volúmenes legibles y BCD presente (FR-022).</summary>
public interface IStructuralVerifier
{
    /// <summary>Comprueba el destino del plan y devuelve los problemas encontrados, si los hay.</summary>
    Task<VerificationResult> VerifyAsync(ClonePlan plan, CancellationToken ct);
}
