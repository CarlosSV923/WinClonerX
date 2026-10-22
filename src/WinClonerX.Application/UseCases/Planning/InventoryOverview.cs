using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.UseCases.Planning;

/// <summary>
/// Resultado de <see cref="IGetInventoryAndEligibility"/>: el inventario leído, el informe global de comprobaciones
/// y la elegibilidad de cada disco distinto del sistema como destino.
/// </summary>
public sealed record InventoryOverview(
    DiskInventory Inventory,
    PreflightReport Global,
    IReadOnlyList<TargetEligibility> Targets);
