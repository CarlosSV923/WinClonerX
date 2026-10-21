namespace WinCopy.Application.UseCases;

/// <summary>Exporta el registro de la ejecución actual a un archivo elegido por la persona (FR-026, FR-027).</summary>
public interface IExportLog
{
    /// <summary>
    /// Escribe un .zip en <paramref name="destinationPath"/> con el archivo .clef de la ejecución y, solo si
    /// <paramref name="includeInventory"/> es verdadero, el inventario de discos. Lanza <see cref="ArgumentException"/> si la ruta
    /// está vacía. Si la escritura falla o se cancela, la excepción sale al llamador y no queda un archivo parcial.
    /// </summary>
    Task ExecuteAsync(string destinationPath, bool includeInventory, CancellationToken ct);
}
