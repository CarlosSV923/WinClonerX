using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Ports;

/// <summary>Registro de operación de una ejecución, en formato CLEF con un evento por línea (R14).</summary>
public interface IExecutionLog
{
    /// <summary>Añade un evento al registro de la ejecución actual.</summary>
    void Record(LogEvent e);

    /// <summary>Ruta del archivo de registro de la ejecución actual.</summary>
    string CurrentFile { get; }

    /// <summary>Exporta el registro, y el inventario si se pide, a un archivo zip en la ruta indicada (FR-026).</summary>
    Task ExportAsync(string destinationPath, bool includeInventory, CancellationToken ct);
}
