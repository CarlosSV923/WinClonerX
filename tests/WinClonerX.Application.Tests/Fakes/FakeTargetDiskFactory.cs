using WinClonerX.Application.Ports;
using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>
/// Fake de ITargetDiskFactory: devuelve siempre el mismo <see cref="FakeTargetDisk"/>. Como el contrato real, rechaza
/// los planes que no están confirmados.
/// </summary>
public sealed class FakeTargetDiskFactory : FakeBase, ITargetDiskFactory
{
    /// <summary>Disco que devuelve Open.</summary>
    public FakeTargetDisk Disk { get; } = new();

    /// <summary>Planes con los que se ha abierto el disco, en orden.</summary>
    public List<PlanId> OpenedPlans { get; } = [];

    /// <summary>Abre el disco del plan confirmado. Lanza InvalidOperationException si el plan no está confirmado.</summary>
    public ITargetDisk Open(ClonePlan confirmedPlan)
    {
        ArgumentNullException.ThrowIfNull(confirmedPlan);
        Enter(nameof(Open));

        if (confirmedPlan.Status != PlanStatus.Confirmed)
        {
            throw new InvalidOperationException("Solo se puede abrir el destino de un plan confirmado.");
        }

        OpenedPlans.Add(confirmedPlan.Id);
        return Disk;
    }
}
