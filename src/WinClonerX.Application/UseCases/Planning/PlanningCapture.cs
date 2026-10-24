using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.UseCases.Planning;

/// <summary>
/// Captura vigente del estado de los discos: el inventario leído y los hechos del entorno con los que se evaluó.
/// Se guardan juntos para que la planificación no mezcle una lectura con los hechos de otra.
/// </summary>
public sealed record PlanningCapture(DiskInventory Inventory, PlatformFacts Facts);
