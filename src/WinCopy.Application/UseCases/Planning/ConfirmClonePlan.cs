using WinCopy.Application.Ports;
using WinCopy.Domain.Planning;

namespace WinCopy.Application.UseCases.Planning;

/// <summary>
/// Caso de uso que confirma un plan. Antes de confirmar vuelve a leer el inventario (FR-011): si la huella de los
/// discos de origen y destino cambió, o alguno de ellos ya no existe, el plan se invalida.
/// </summary>
public sealed class ConfirmClonePlan(
    IClonePlanStore store,
    IDiskInventoryReader inventoryReader,
    IExecutionLog log,
    IClock clock) : IConfirmClonePlan
{
    private const string DisksChangedReason = "DisksChanged";

    /// <inheritdoc />
    public async Task<ClonePlan> ExecuteAsync(
        PlanId id,
        IReadOnlyCollection<PreflightCode> acceptedWarnings,
        string typedSuffix,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(acceptedWarnings);
        ArgumentNullException.ThrowIfNull(typedSuffix);

        if (!store.TryGet(id, out var plan))
        {
            throw new InvalidOperationException("No existe un plan de clonado con ese identificador.");
        }

        if (plan.Status == PlanStatus.Invalidated)
        {
            throw new PlanInvalidatedException(id, plan.InvalidationReason ?? DisksChangedReason);
        }

        var fresh = await inventoryReader.ReadAsync(ct);
        var target = fresh.FindDisk(plan.Target);
        if (target is null
            || fresh.FindDisk(plan.Source) is null
            || fresh.Fingerprint(plan.Source, plan.Target) != plan.Fingerprint)
        {
            plan.Invalidate(DisksChangedReason);
            store.Save(plan);
            log.Record(PlanningLogEvents.PlanInvalidated(id, DisksChangedReason));
            throw new PlanInvalidatedException(id, DisksChangedReason);
        }

        plan.AcceptWarnings(acceptedWarnings);
        plan.Confirm(new TargetConfirmation(plan.Target, typedSuffix, clock.UtcNow), target);
        store.Save(plan);
        log.Record(PlanningLogEvents.PlanConfirmed(plan));

        return plan;
    }
}
