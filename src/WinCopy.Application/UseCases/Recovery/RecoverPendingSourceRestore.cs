using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Application.UseCases.Execution;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.UseCases.Recovery;

/// <summary>
/// Recuperación al iniciarse (FR-021a). Lee el diario de restauración del origen y, si hay una entrada pendiente, la valida
/// contra el inventario actual antes de tocar nada. La partición debe existir en el disco del sistema y ser su partición de
/// Windows, y la longitud original debe ser mayor que la actual y no superar el máximo que Windows admite. Si todo encaja, la
/// restaura, comprueba su longitud y borra el diario. Si ya tiene su longitud original, solo borra el diario. En cualquier otro
/// caso no toca la partición, conserva el diario y devuelve <see cref="PendingSourceRestoreOutcome.NeedsAttention"/>.
/// </summary>
/// <remarks>
/// El registro se pide con una función y se resuelve solo cuando hay algo que registrar, para no crear un archivo de registro
/// en cada arranque. Ningún fallo sale de este caso de uso: la aplicación debe poder arrancar aunque el disco esté raro.
/// </remarks>
public sealed class RecoverPendingSourceRestore(
    IDiskInventoryReader inventoryReader,
    ISourceResizer resizer,
    ISourceRestoreJournal journal,
    Func<IExecutionLog> log) : IRecoverPendingSourceRestore
{
    private const long Mib = 1L << 20;
    private const string RestoredResult = "Restored";
    private const string AlreadyRestoredResult = "AlreadyRestored";

    /// <inheritdoc />
    public async Task<PendingSourceRestoreRecovery> RecoverAsync(CancellationToken ct)
    {
        var read = ReadJournal();

        if (read.State == PendingSourceRestoreState.Absent)
        {
            return PendingSourceRestoreRecovery.Nothing;
        }

        if (read.State == PendingSourceRestoreState.Invalid)
        {
            Record(ExecutionLogEvents.PendingSourceRestoreFound(null, read.Detail));
            return Fail(null, PendingSourceRestoreIssue.JournalUnreadable, read.Detail);
        }

        var entry = read.Entry ?? throw new InvalidOperationException("Una entrada válida del diario debe tener contenido.");
        Record(ExecutionLogEvents.PendingSourceRestoreFound(entry, null));

        try
        {
            return await ValidateAndRestoreAsync(entry, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(entry, PendingSourceRestoreIssue.UnexpectedError, ex.ToString());
        }
    }

    private async Task<PendingSourceRestoreRecovery> ValidateAndRestoreAsync(PendingSourceRestore entry, CancellationToken ct)
    {
        DiskInventory inventory;
        try
        {
            inventory = await inventoryReader.ReadAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(entry, PendingSourceRestoreIssue.InventoryUnavailable, ex.ToString());
        }

        var disk = inventory.SystemDisk;
        if (disk is null)
        {
            return Fail(entry, PendingSourceRestoreIssue.InventoryUnavailable, "El inventario no tiene un disco del sistema: " + inventory.UnsupportedReason);
        }

        var partition = disk.Partitions.FirstOrDefault(candidate => candidate.UniqueId == entry.Partition);
        if (partition is null)
        {
            return Fail(entry, PendingSourceRestoreIssue.PartitionNotFound, $"La partición {entry.Partition.Value:D} no está en el disco del sistema.");
        }

        if (partition.Role != PartitionRole.Windows)
        {
            return Fail(entry, PendingSourceRestoreIssue.NotWindowsPartition, $"La partición {entry.Partition.Value:D} no es la partición de Windows del disco del sistema.");
        }

        var current = new ByteSize(partition.Extent.Length);
        if (IsAtOriginalLength(current, entry.OriginalLength))
        {
            ClearJournal(entry);
            Record(ExecutionLogEvents.PendingSourceRestoreCompleted(entry, AlreadyRestoredResult));
            return PendingSourceRestoreRecovery.AlreadyRestored;
        }

        if (entry.OriginalLength < current)
        {
            return Fail(
                entry,
                PendingSourceRestoreIssue.OriginalNotLarger,
                $"La longitud original ({entry.OriginalLength.Bytes} bytes) no es mayor que la actual ({current.Bytes} bytes).");
        }

        ByteSize maximum;
        try
        {
            maximum = await resizer.GetMaximumSizeAsync(entry.Partition, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(entry, PendingSourceRestoreIssue.SizeQueryFailed, ex.ToString());
        }

        if (entry.OriginalLength > maximum)
        {
            return Fail(
                entry,
                PendingSourceRestoreIssue.ExceedsSupportedMaximum,
                $"La longitud original ({entry.OriginalLength.Bytes} bytes) supera el máximo que admite Windows ({maximum.Bytes} bytes).");
        }

        try
        {
            await resizer.RestoreAsync(entry.Partition, entry.OriginalLength, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(entry, PendingSourceRestoreIssue.RestoreFailed, ex.ToString());
        }

        ByteSize? restored;
        try
        {
            restored = await CurrentLengthAsync(entry.Partition, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(entry, PendingSourceRestoreIssue.VerificationFailed, ex.ToString());
        }

        if (restored is not { } length)
        {
            return Fail(entry, PendingSourceRestoreIssue.VerificationFailed, "Tras la restauración la partición ya no aparece en el disco del sistema.");
        }

        if (!IsAtOriginalLength(length, entry.OriginalLength))
        {
            return Fail(
                entry,
                PendingSourceRestoreIssue.VerificationFailed,
                $"Tras la restauración la partición mide {length.Bytes} bytes en lugar de {entry.OriginalLength.Bytes}.");
        }

        ClearJournal(entry);
        Record(ExecutionLogEvents.PendingSourceRestoreCompleted(entry, RestoredResult));
        return PendingSourceRestoreRecovery.Restored;
    }

    /// <summary>Longitud actual de la partición en el disco del sistema, o null si ya no aparece.</summary>
    private async Task<ByteSize?> CurrentLengthAsync(PartitionGuid partition, CancellationToken ct)
    {
        var inventory = await inventoryReader.ReadAsync(ct);
        var current = inventory.SystemDisk?.Partitions.FirstOrDefault(candidate => candidate.UniqueId == partition);

        return current is null ? null : new ByteSize(current.Extent.Length);
    }

    /// <summary>
    /// Windows redondea la longitud a MiB completos (igual que <c>StorageSourceResizer</c>), así que se acepta una longitud entre
    /// la original y su redondeo a MiB.
    /// </summary>
    private static bool IsAtOriginalLength(ByteSize current, ByteSize original) =>
        current.Bytes >= original.Bytes && current.Bytes <= RoundUpToMib(original.Bytes);

    private static long RoundUpToMib(long value)
    {
        long remainder = value % Mib;
        return remainder == 0 ? value : value + (Mib - remainder);
    }

    private PendingSourceRestoreRead ReadJournal()
    {
        try
        {
            return journal.Read();
        }
        catch (Exception ex)
        {
            return PendingSourceRestoreRead.Invalid(ex.ToString());
        }
    }

    /// <summary>Borra la entrada. Un fallo es un aviso: la partición ya tiene su longitud original.</summary>
    private void ClearJournal(PendingSourceRestore entry)
    {
        try
        {
            journal.Clear();
            Record(ExecutionLogEvents.SourceRestoreJournalCleared(entry.Partition));
        }
        catch (Exception ex)
        {
            Record(ExecutionLogEvents.SourceRestoreJournalClearFailed(entry.Partition, ex.ToString()));
        }
    }

    /// <summary>Registra el motivo y devuelve el desenlace. El diario se conserva: lo decide el llamador.</summary>
    private PendingSourceRestoreRecovery Fail(PendingSourceRestore? entry, PendingSourceRestoreIssue issue, string? detail)
    {
        Record(ExecutionLogEvents.PendingSourceRestoreFailed(entry, issue.ToString(), detail));
        return PendingSourceRestoreRecovery.NeedsAttention(issue, detail);
    }

    /// <summary>Escribe en el registro sin dejar que un fallo del registro impida la recuperación.</summary>
    private void Record(LogEvent e)
    {
        try
        {
            log().Record(e);
        }
        catch (Exception)
        {
            // A propósito: un registro que no se puede escribir no debe impedir arrancar ni restaurar la partición.
        }
    }
}
