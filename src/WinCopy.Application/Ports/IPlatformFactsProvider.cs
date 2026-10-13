using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Ports;

/// <summary>Obtiene los hechos del entorno que necesitan las comprobaciones previas.</summary>
public interface IPlatformFactsProvider
{
    /// <summary>Reúne firmware, BitLocker por volumen, batería, VSS, espacio de diferencias, mínimo de reducción y si el destino candidato está en uso.</summary>
    Task<PlatformFacts> GetAsync(DiskInventory inventory, DiskId? candidateTarget, CancellationToken ct);
}
