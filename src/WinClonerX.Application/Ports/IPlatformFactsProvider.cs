using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.Ports;

/// <summary>Obtiene los hechos del entorno que necesitan las comprobaciones previas.</summary>
public interface IPlatformFactsProvider
{
    /// <summary>Reúne BitLocker por volumen, batería, VSS, espacio de diferencias, mínimo de reducción y si cada disco candidato está en uso.</summary>
    Task<PlatformFacts> GetAsync(DiskInventory inventory, CancellationToken ct);
}
