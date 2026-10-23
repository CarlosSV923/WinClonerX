using System.Globalization;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.UseCases.Execution;

/// <summary>
/// Construye los eventos de ejecución del registro de operación (contracts/log-events.md). No añaden <c>ExecutionId</c>: el
/// adaptador de registro lo escribe en la cabecera de cada línea, junto con <c>AppVersion</c>. Los valores son primitivos o
/// textos, para que se serialicen igual en CLEF.
/// </summary>
internal static class ExecutionLogEvents
{
    /// <summary>La ejecución ha empezado para el plan indicado.</summary>
    public static LogEvent Started(PlanId plan) =>
        Create("ExecutionStarted", LogLevelKind.Information, ("PlanId", Text(plan.Value)));

    /// <summary>Ha empezado una fase.</summary>
    public static LogEvent PhaseStarted(ExecutionPhase phase) =>
        Create("PhaseStarted", LogLevelKind.Information, ("Phase", phase.ToString()));

    /// <summary>Ha terminado una fase, tras la duración indicada.</summary>
    public static LogEvent PhaseCompleted(ExecutionPhase phase, TimeSpan duration) =>
        Create("PhaseCompleted", LogLevelKind.Information, ("Phase", phase.ToString()), ("DurationMs", Milliseconds(duration)));

    /// <summary>A person requested cancellation while the run still accepted it.</summary>
    public static LogEvent CancelRequested(ExecutionPhase phase, bool pending) =>
        Create("CancelRequested", LogLevelKind.Information, ("Phase", phase.ToString()), ("CancelPending", pending));

    /// <summary>Avance de la copia medido, con su rendimiento y el tiempo restante si ya se conoce.</summary>
    public static LogEvent ProgressReported(CloneProgress progress) => Create(
        "ProgressReported",
        LogLevelKind.Information,
        ("BytesDone", progress.BytesDone),
        ("BytesTotal", progress.BytesTotal),
        ("ThroughputBps", progress.ThroughputBytesPerSecond),
        ("EtaSeconds", progress.Eta is { } eta ? eta.TotalSeconds : (double?)null));

    /// <summary>Una compensación ha terminado. Un fallo se registra como advertencia y no oculta el error original.</summary>
    public static LogEvent CompensationCompleted(CompensationKind kind, bool success, string? detail) => Create(
        "CompensationCompleted",
        success ? LogLevelKind.Information : LogLevelKind.Warning,
        ("Compensation", kind.ToString()),
        ("Result", success ? "Succeeded" : "Failed"),
        ("TechnicalDetail", detail));

    /// <summary>La ejecución ha completado todas sus fases. El detalle opcional recoge avisos no bloqueantes, como WinRE.</summary>
    public static LogEvent Completed(TimeSpan duration, string? technicalDetail) => Create(
        "ExecutionCompleted",
        LogLevelKind.Information,
        ("Outcome", CloneOutcome.Completed.ToString()),
        ("TechnicalDetail", technicalDetail),
        ("DurationMs", Milliseconds(duration)));

    /// <summary>La ejecución ha terminado tras cancelarse y completar las compensaciones.</summary>
    public static LogEvent Cancelled(TimeSpan duration) => Create(
        "ExecutionCancelled",
        LogLevelKind.Warning,
        ("Outcome", CloneOutcome.Cancelled.ToString()),
        ("DurationMs", Milliseconds(duration)));

    /// <summary>La ejecución ha fallado en una fase, con su código estable y el detalle técnico.</summary>
    public static LogEvent Failed(TimeSpan duration, ExecutionFailure failure) => Create(
        "ExecutionFailed",
        LogLevelKind.Error,
        ("Outcome", CloneOutcome.Failed.ToString()),
        ("FailureCode", failure.Code),
        ("Phase", failure.Phase.ToString()),
        ("TechnicalDetail", failure.TechnicalDetail),
        ("DurationMs", Milliseconds(duration)));

    /// <summary>El plan se invalida porque cambió el estado de los discos antes de ejecutarlo (FR-011).</summary>
    public static LogEvent PlanInvalidated(PlanId plan, string reason) => Create(
        "PlanInvalidated",
        LogLevelKind.Warning,
        ("PlanId", Text(plan.Value)),
        ("Reason", reason));

    /// <summary>
    /// Un paso de liberación de recursos ha fallado (montajes, instantánea o destino). No cambia el desenlace de la ejecución:
    /// <c>Step</c> indica cuál ha fallado.
    /// </summary>
    public static LogEvent CleanupFailed(string step, string technicalDetail) => Create(
        "CleanupFailed",
        LogLevelKind.Warning,
        ("Step", step),
        ("TechnicalDetail", technicalDetail));

    /// <summary>
    /// La restauración del tamaño original del origen ha fallado tras un clon verificado. No cambia el desenlace (el clon es
    /// válido y la ejecución termina como Completed con SourceRestored = false), pero la partición del origen queda reducida.
    /// </summary>
    public static LogEvent SourceRestoreFailed(PartitionGuid partition, ByteSize originalLength, string technicalDetail) => Create(
        "SourceRestoreFailed",
        LogLevelKind.Warning,
        ("Partition", Text(partition.Value)),
        ("OriginalLength", originalLength.Bytes),
        ("TechnicalDetail", technicalDetail));

    /// <summary>Se ha escrito la entrada del diario antes de reducir el origen; sin ella no se reduce (FR-021a).</summary>
    public static LogEvent SourceRestoreJournalWritten(PendingSourceRestore entry) => Create(
        "SourceRestoreJournalWritten",
        LogLevelKind.Information,
        ("Partition", Text(entry.Partition.Value)),
        ("OriginalLength", entry.OriginalLength.Bytes),
        ("PlanId", Text(entry.Plan.Value)));

    /// <summary>La entrada del diario se ha borrado porque el origen ya tiene su longitud original.</summary>
    public static LogEvent SourceRestoreJournalCleared(PartitionGuid partition) => Create(
        "SourceRestoreJournalCleared",
        LogLevelKind.Information,
        ("Partition", Text(partition.Value)));

    /// <summary>
    /// No se ha podido borrar la entrada del diario. Es un aviso y no cambia el desenlace: el origen ya tiene su longitud
    /// original, y la siguiente ejecución encontrará la entrada, la comprobará y la borrará.
    /// </summary>
    public static LogEvent SourceRestoreJournalClearFailed(PartitionGuid partition, string technicalDetail) => Create(
        "SourceRestoreJournalClearFailed",
        LogLevelKind.Warning,
        ("Partition", Text(partition.Value)),
        ("TechnicalDetail", technicalDetail));

    /// <summary>
    /// Al iniciarse hay una restauración pendiente del origen. Si el diario no se puede leer, <paramref name="entry"/> es nulo y
    /// <paramref name="technicalDetail"/> explica por qué.
    /// </summary>
    public static LogEvent PendingSourceRestoreFound(PendingSourceRestore? entry, string? technicalDetail) => Create(
        "PendingSourceRestoreFound",
        LogLevelKind.Warning,
        ("Partition", entry is null ? null : Text(entry.Partition.Value)),
        ("OriginalLength", entry?.OriginalLength.Bytes),
        ("PlanId", entry is null ? null : Text(entry.Plan.Value)),
        ("RecordedAt", entry?.RecordedAt),
        ("TechnicalDetail", technicalDetail));

    /// <summary>La restauración pendiente se ha resuelto: <paramref name="result"/> es Restored o AlreadyRestored.</summary>
    public static LogEvent PendingSourceRestoreCompleted(PendingSourceRestore entry, string result) => Create(
        "PendingSourceRestoreCompleted",
        LogLevelKind.Information,
        ("Partition", Text(entry.Partition.Value)),
        ("OriginalLength", entry.OriginalLength.Bytes),
        ("Result", result));

    /// <summary>
    /// La restauración pendiente no se ha podido resolver al iniciarse y necesita atención. <paramref name="reason"/> es el código
    /// de la comprobación que falló. El diario se conserva para el siguiente inicio.
    /// </summary>
    public static LogEvent PendingSourceRestoreFailed(PendingSourceRestore? entry, string reason, string? technicalDetail) => Create(
        "PendingSourceRestoreFailed",
        LogLevelKind.Warning,
        ("Partition", entry is null ? null : Text(entry.Partition.Value)),
        ("OriginalLength", entry?.OriginalLength.Bytes),
        ("Reason", reason),
        ("TechnicalDetail", technicalDetail));

    /// <summary>
    /// La verificación completa ha terminado. Un resultado distinto es un error de la ejecución (VerificationFailed); los
    /// desplazamientos son los primeros bytes distintos del disco de destino, como mucho diez.
    /// </summary>
    public static LogEvent FullVerificationCompleted(VerificationResult result) => Create(
        "FullVerificationCompleted",
        result.Passed ? LogLevelKind.Information : LogLevelKind.Error,
        ("Passed", result.Passed),
        ("DifferingBytes", result.DifferingBytes ?? 0),
        ("FirstDifferenceOffsets", (result.FirstDifferenceOffsets ?? Array.Empty<long>()).ToArray()),
        ("Issues", result.Issues.ToArray()));

    /// <summary>Registra una muestra de una lectura que produjo diferencias en una partición.</summary>
    public static LogEvent FullVerificationMismatchSample(
        string partitionRole,
        string newPartitionGuid,
        ChunkMismatchSample sample) => Create(
        "FullVerificationMismatchSample",
        LogLevelKind.Warning,
        ("PartitionRole", partitionRole),
        ("NewPartitionGuid", newPartitionGuid),
        ("SourceOffset", sample.SourceOffset),
        ("Length", sample.Length),
        ("DifferingBytes", sample.DifferingBytes),
        ("SourceStable", sample.SourceStable),
        ("TargetStable", sample.TargetStable),
        ("SecondReadDiffering", sample.SecondReadDiffering),
        ("TargetAllZero", sample.TargetAllZero),
        ("SourceAllZero", sample.SourceAllZero),
        ("Shift", sample.Shift),
        ("SourceSample", sample.SourceSample),
        ("TargetSample", sample.TargetSample));

    /// <summary>
    /// No se pudo activar la vigilancia de la desconexión de los discos. El clonado sigue: una desconexión se detecta igualmente
    /// por el error de E/S que provoca.
    /// </summary>
    public static LogEvent DeviceMonitorUnavailable(string technicalDetail) => Create(
        "DeviceMonitorUnavailable",
        LogLevelKind.Warning,
        ("TechnicalDetail", technicalDetail));

    private static LogEvent Create(string name, LogLevelKind level, params (string Key, object? Value)[] entries)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            properties[key] = value;
        }

        return new LogEvent(name, properties, level);
    }

    private static long Milliseconds(TimeSpan duration) => (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);

    private static string Text(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
}
