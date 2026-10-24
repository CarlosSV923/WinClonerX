using System.Diagnostics.CodeAnalysis;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.UseCases.Planning;

/// <summary>
/// Implementación en memoria de <see cref="IClonePlanStore"/>. Es segura para uso concurrente: todos los accesos
/// se hacen bajo el mismo cerrojo. Se registra como singleton, porque la captura y los planes viven mientras
/// dura la sesión de la aplicación.
/// </summary>
public sealed class ClonePlanStore : IClonePlanStore
{
    private readonly object _gate = new();
    private readonly Dictionary<PlanId, ClonePlan> _plans = new();
    private PlanningCapture? _capture;

    /// <inheritdoc />
    public PlanningCapture? Capture
    {
        get
        {
            lock (_gate)
            {
                return _capture;
            }
        }
    }

    /// <inheritdoc />
    public DiskInventory? CurrentInventory => Capture?.Inventory;

    /// <inheritdoc />
    public void SetCapture(PlanningCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);

        lock (_gate)
        {
            _capture = capture;
        }
    }

    /// <inheritdoc />
    public void Save(ClonePlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);

        lock (_gate)
        {
            _plans[plan.Id] = plan;
        }
    }

    /// <inheritdoc />
    public bool TryGet(PlanId id, [NotNullWhen(true)] out ClonePlan? plan)
    {
        lock (_gate)
        {
            return _plans.TryGetValue(id, out plan);
        }
    }

    /// <inheritdoc />
    public bool Remove(PlanId id)
    {
        lock (_gate)
        {
            return _plans.Remove(id);
        }
    }
}
