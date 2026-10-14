using WinCopy.Domain.Planning;

namespace WinCopy.Application.Ports;

/// <summary>Abre el disco de destino de un plan. Lanza una excepción si el plan no está confirmado o el destino es el disco del sistema.</summary>
public interface ITargetDiskFactory
{
    /// <summary>Abre el destino del plan confirmado indicado.</summary>
    ITargetDisk Open(ClonePlan confirmedPlan);
}
