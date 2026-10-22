using WinClonerX.Application.Ports;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.UseCases.Planning;

/// <summary>
/// Caso de uso que planifica el clonado usando la captura guardada por <see cref="GetInventoryAndEligibility"/>.
/// El generador de GUID se inyecta para que las pruebas sean deterministas; en producción usa <see cref="Guid.NewGuid()"/>.
/// </summary>
public sealed class BuildClonePlan(IClonePlanStore store, IExecutionLog log, Func<Guid>? newGuid = null) : IBuildClonePlan
{
    private readonly Func<Guid> _newGuid = newGuid ?? Guid.NewGuid;

    /// <inheritdoc />
    public Task<Result<ClonePlan, PreflightReport>> ExecuteAsync(DiskId target, VerificationMode mode, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Task.FromCanceled<Result<ClonePlan, PreflightReport>>(ct);
        }

        var capture = store.Capture
            ?? throw new InvalidOperationException("No hay un inventario capturado. Hay que leer el inventario antes de planificar.");

        var result = ClonePlanner.Plan(capture.Inventory, target, capture.Facts, mode, CapacityPolicy.Default, _newGuid);
        if (result.IsSuccess)
        {
            var plan = result.Value;
            store.Save(plan);
            log.Record(PlanningLogEvents.PlanCreated(plan));
        }

        return Task.FromResult(result);
    }
}
