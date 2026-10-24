using WinClonerX.Domain.Inventory;

namespace WinClonerX.Application.Ports;

/// <summary>Lee el inventario de discos, particiones y volúmenes del equipo.</summary>
public interface IDiskInventoryReader
{
    /// <summary>Captura el inventario completo en este instante.</summary>
    Task<DiskInventory> ReadAsync(CancellationToken ct);
}
