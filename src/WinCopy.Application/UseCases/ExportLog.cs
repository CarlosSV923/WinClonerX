using WinCopy.Application.Ports;

namespace WinCopy.Application.UseCases;

/// <summary>
/// Caso de uso de exportación del registro. Es deliberadamente fino: valida la ruta y delega en el registro de la ejecución
/// actual. Los errores de escritura salen tal cual al llamador.
/// </summary>
/// <remarks>
/// El registro se pide con una función y se resuelve solo al exportar, igual que en <c>RecoverPendingSourceRestore</c>: así,
/// construir el caso de uso no crea un archivo de registro en cada arranque.
/// </remarks>
public sealed class ExportLog(Func<IExecutionLog> log) : IExportLog
{
    /// <inheritdoc />
    public async Task ExecuteAsync(string destinationPath, bool includeInventory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        await log().ExportAsync(destinationPath, includeInventory, ct);
    }
}
