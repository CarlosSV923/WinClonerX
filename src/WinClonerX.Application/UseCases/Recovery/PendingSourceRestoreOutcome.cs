namespace WinClonerX.Application.UseCases.Recovery;

/// <summary>Desenlace de la recuperación de la restauración pendiente del origen al iniciarse.</summary>
public enum PendingSourceRestoreOutcome
{
    /// <summary>No había ninguna restauración pendiente.</summary>
    Nothing,

    /// <summary>La partición estaba reducida y se ha restaurado a su longitud original. El diario se ha borrado.</summary>
    Restored,

    /// <summary>La partición ya tenía su longitud original. Solo se ha borrado el diario, sin tocar la partición.</summary>
    AlreadyRestored,

    /// <summary>La restauración no se ha hecho porque una comprobación falló, o porque la restauración falló. El diario se conserva.</summary>
    NeedsAttention,
}
