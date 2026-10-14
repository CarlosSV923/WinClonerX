using System.Diagnostics.CodeAnalysis;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;

namespace WinCopy.Application.UseCases.Planning;

/// <summary>Estado de la planificación en memoria: la última captura de discos y los planes por identificador.</summary>
public interface IClonePlanStore
{
    /// <summary>Última captura (inventario y hechos), o null si todavía no se ha leído el inventario.</summary>
    PlanningCapture? Capture { get; }

    /// <summary>Inventario de la última captura, o null si no hay captura.</summary>
    DiskInventory? CurrentInventory { get; }

    /// <summary>Sustituye la captura vigente.</summary>
    void SetCapture(PlanningCapture capture);

    /// <summary>Guarda el plan, o sustituye el guardado con el mismo identificador.</summary>
    void Save(ClonePlan plan);

    /// <summary>Busca un plan por su identificador.</summary>
    bool TryGet(PlanId id, [NotNullWhen(true)] out ClonePlan? plan);

    /// <summary>Quita un plan. Devuelve false si no existía.</summary>
    bool Remove(PlanId id);
}
