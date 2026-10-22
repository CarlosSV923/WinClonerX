using WinClonerX.Application.Ports;
using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.UseCases.Planning;

/// <summary>
/// Caso de uso que lee el inventario y los hechos del entorno, evalúa las comprobaciones previas con la política por
/// defecto, guarda la captura para la planificación posterior y registra el inventario y la evaluación en el log.
/// </summary>
public sealed class GetInventoryAndEligibility(
    IDiskInventoryReader inventoryReader,
    IPlatformFactsProvider factsProvider,
    IClonePlanStore store,
    IExecutionLog log) : IGetInventoryAndEligibility
{
    /// <inheritdoc />
    public async Task<InventoryOverview> ExecuteAsync(CancellationToken ct)
    {
        var inventory = await inventoryReader.ReadAsync(ct);
        var facts = await factsProvider.GetAsync(inventory, ct);

        var (global, targets) = PreflightEvaluator.Evaluate(inventory, facts, CapacityPolicy.Default);

        store.SetCapture(new PlanningCapture(inventory, facts));
        log.Record(PlanningLogEvents.InventoryCaptured(inventory));
        log.Record(PlanningLogEvents.PreflightEvaluated(global, targets));

        return new InventoryOverview(inventory, global, targets);
    }
}
