namespace WinClonerX.Application.UseCases.Recovery;

/// <summary>
/// Recupera al iniciarse la partición de Windows del origen si una ejecución anterior murió con ella reducida. Nunca lanza
/// excepciones por el estado del disco: cualquier duda se devuelve como <see cref="PendingSourceRestoreOutcome.NeedsAttention"/>.
/// </summary>
public interface IRecoverPendingSourceRestore
{
    /// <summary>Lee el diario, valida la entrada contra el inventario actual y restaura la partición si todo encaja.</summary>
    Task<PendingSourceRestoreRecovery> RecoverAsync(CancellationToken ct);
}
