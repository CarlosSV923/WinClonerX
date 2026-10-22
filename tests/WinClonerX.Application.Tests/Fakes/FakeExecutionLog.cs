using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de IExecutionLog: guarda los eventos en memoria y registra las rutas de exportación, sin tocar el disco.</summary>
public sealed class FakeExecutionLog : FakeBase, IExecutionLog
{
    /// <summary>Eventos registrados, en orden.</summary>
    public List<LogEvent> Events { get; } = [];

    /// <summary>Ruta del archivo de registro que devuelve la propiedad CurrentFile.</summary>
    public string CurrentFile { get; set; } = @"C:\ProgramData\WinClonerX\logs\winClonerX-test.clef";

    /// <summary>Exportaciones solicitadas: ruta de destino e indicador de inventario.</summary>
    public List<(string DestinationPath, bool IncludeInventory)> Exports { get; } = [];

    /// <summary>Guarda el evento en la lista.</summary>
    public void Record(LogEvent e)
    {
        Enter(nameof(Record));
        Events.Add(e);
    }

    /// <summary>Registra la ruta de exportación solicitada.</summary>
    public Task ExportAsync(string destinationPath, bool includeInventory, CancellationToken ct) =>
        Complete(nameof(ExportAsync), ct, () => Exports.Add((destinationPath, includeInventory)));
}
