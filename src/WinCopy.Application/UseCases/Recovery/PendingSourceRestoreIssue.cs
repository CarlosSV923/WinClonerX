namespace WinCopy.Application.UseCases.Recovery;

/// <summary>
/// Motivo por el que la recuperación no restaura la partición (<see cref="PendingSourceRestoreOutcome.NeedsAttention"/>). Cada
/// valor es una comprobación que se hace antes de tocar el disco, o el fallo de la propia restauración.
/// </summary>
public enum PendingSourceRestoreIssue
{
    /// <summary>Sin motivo: la recuperación no ha necesitado atención.</summary>
    None,

    /// <summary>El archivo del diario existe pero está dañado o no se puede leer.</summary>
    JournalUnreadable,

    /// <summary>El inventario no es utilizable: sin disco del sistema, o la lectura ha fallado.</summary>
    InventoryUnavailable,

    /// <summary>La partición del diario no existe en el disco del sistema.</summary>
    PartitionNotFound,

    /// <summary>La partición del diario no es la partición de Windows del disco del sistema.</summary>
    NotWindowsPartition,

    /// <summary>La longitud original del diario es menor que la actual: la partición no está reducida como se esperaba.</summary>
    OriginalNotLarger,

    /// <summary>La longitud original supera el tamaño máximo que Windows admite para la partición.</summary>
    ExceedsSupportedMaximum,

    /// <summary>No se ha podido consultar el tamaño máximo que Windows admite.</summary>
    SizeQueryFailed,

    /// <summary>La llamada de restauración ha fallado.</summary>
    RestoreFailed,

    /// <summary>Tras la restauración, la partición no tiene la longitud original.</summary>
    VerificationFailed,

    /// <summary>Un fallo inesperado durante la recuperación. Se trata como atención necesaria, sin cerrar la aplicación.</summary>
    UnexpectedError,
}
