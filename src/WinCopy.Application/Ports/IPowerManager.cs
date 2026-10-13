using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Ports;

/// <summary>Gestiona la suspensión del equipo y consulta su estado de energía (R10).</summary>
public interface IPowerManager
{
    /// <summary>Impide la suspensión mientras el objeto devuelto no se libere.</summary>
    IDisposable PreventSleep();

    /// <summary>Indica si el equipo funciona con batería y el porcentaje restante, si se conoce.</summary>
    Task<PowerStatus> GetStatusAsync(CancellationToken ct);
}
