namespace WinCopy.Application.Ports.Models;

/// <summary>Estado del diario de restauración: no hay entrada, hay una entrada válida o hay un archivo que no se puede leer.</summary>
public enum PendingSourceRestoreState
{
    /// <summary>No hay ninguna entrada pendiente.</summary>
    Absent,

    /// <summary>Hay una entrada pendiente válida.</summary>
    Present,

    /// <summary>Hay un archivo de diario, pero está dañado o no se puede leer.</summary>
    Invalid,
}

/// <summary>
/// Resultado de leer el diario. Un archivo dañado no lanza excepción: se devuelve como <see cref="PendingSourceRestoreState.Invalid"/>
/// con su detalle, para que quien lo lee decida, sin tocar el origen.
/// </summary>
public sealed record PendingSourceRestoreRead(PendingSourceRestoreState State, PendingSourceRestore? Entry, string? Detail)
{
    /// <summary>No hay entrada pendiente.</summary>
    public static PendingSourceRestoreRead Absent { get; } = new(PendingSourceRestoreState.Absent, null, null);

    /// <summary>Hay una entrada pendiente válida.</summary>
    public static PendingSourceRestoreRead Present(PendingSourceRestore entry) => new(PendingSourceRestoreState.Present, entry, null);

    /// <summary>Hay un archivo de diario que no se puede usar; <paramref name="detail"/> explica por qué.</summary>
    public static PendingSourceRestoreRead Invalid(string detail) => new(PendingSourceRestoreState.Invalid, null, detail);
}
