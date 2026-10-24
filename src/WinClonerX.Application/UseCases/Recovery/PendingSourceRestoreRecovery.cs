namespace WinClonerX.Application.UseCases.Recovery;

/// <summary>
/// Resultado de la recuperación. <see cref="Issue"/> y <see cref="Detail"/> solo tienen valor cuando el desenlace es
/// <see cref="PendingSourceRestoreOutcome.NeedsAttention"/>; <see cref="Detail"/> es texto técnico para el registro.
/// </summary>
public sealed record PendingSourceRestoreRecovery(PendingSourceRestoreOutcome Outcome, PendingSourceRestoreIssue Issue, string? Detail)
{
    /// <summary>No había ninguna restauración pendiente.</summary>
    public static PendingSourceRestoreRecovery Nothing { get; } = new(PendingSourceRestoreOutcome.Nothing, PendingSourceRestoreIssue.None, null);

    /// <summary>La partición se ha restaurado.</summary>
    public static PendingSourceRestoreRecovery Restored { get; } = new(PendingSourceRestoreOutcome.Restored, PendingSourceRestoreIssue.None, null);

    /// <summary>La partición ya tenía su longitud original.</summary>
    public static PendingSourceRestoreRecovery AlreadyRestored { get; } = new(PendingSourceRestoreOutcome.AlreadyRestored, PendingSourceRestoreIssue.None, null);

    /// <summary>Hay que atender la restauración pendiente. El diario se conserva.</summary>
    public static PendingSourceRestoreRecovery NeedsAttention(PendingSourceRestoreIssue issue, string? detail) =>
        new(PendingSourceRestoreOutcome.NeedsAttention, issue, detail);
}
