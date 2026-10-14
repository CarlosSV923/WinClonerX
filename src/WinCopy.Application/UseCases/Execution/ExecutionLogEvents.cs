using System.Globalization;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.UseCases.Execution;

/// <summary>
/// Construye los eventos de ejecución del registro de operación (contracts/log-events.md). Todos llevan la propiedad
/// <c>ExecutionId</c>. Los valores son primitivos o textos, para que se serialicen igual en CLEF.
/// </summary>
internal static class ExecutionLogEvents
{
    /// <summary>La ejecución ha empezado para el plan indicado.</summary>
    public static LogEvent Started(ExecutionId id, PlanId plan) =>
        Create("ExecutionStarted", id, LogLevelKind.Information, ("PlanId", Text(plan.Value)));

    /// <summary>Ha empezado una fase.</summary>
    public static LogEvent PhaseStarted(ExecutionId id, ExecutionPhase phase) =>
        Create("PhaseStarted", id, LogLevelKind.Information, ("Phase", phase.ToString()));

    /// <summary>Ha terminado una fase, tras la duración indicada.</summary>
    public static LogEvent PhaseCompleted(ExecutionId id, ExecutionPhase phase, TimeSpan duration) =>
        Create("PhaseCompleted", id, LogLevelKind.Information, ("Phase", phase.ToString()), ("DurationMs", Milliseconds(duration)));

    /// <summary>Avance de la copia medido, con su rendimiento y el tiempo restante si ya se conoce.</summary>
    public static LogEvent ProgressReported(ExecutionId id, CloneProgress progress) => Create(
        "ProgressReported",
        id,
        LogLevelKind.Information,
        ("BytesDone", progress.BytesDone),
        ("BytesTotal", progress.BytesTotal),
        ("ThroughputBps", progress.ThroughputBytesPerSecond),
        ("EtaSeconds", progress.Eta is { } eta ? eta.TotalSeconds : (double?)null));

    /// <summary>Una compensación ha terminado. Un fallo se registra como advertencia y no oculta el error original.</summary>
    public static LogEvent CompensationCompleted(ExecutionId id, CompensationKind kind, bool success, string? detail) => Create(
        "CompensationCompleted",
        id,
        success ? LogLevelKind.Information : LogLevelKind.Warning,
        ("Compensation", kind.ToString()),
        ("Result", success ? "Succeeded" : "Failed"),
        ("TechnicalDetail", detail));

    /// <summary>La ejecución ha completado todas sus fases. El detalle opcional recoge avisos no bloqueantes, como WinRE.</summary>
    public static LogEvent Completed(ExecutionId id, TimeSpan duration, string? technicalDetail) => Create(
        "ExecutionCompleted",
        id,
        LogLevelKind.Information,
        ("Outcome", CloneOutcome.Completed.ToString()),
        ("TechnicalDetail", technicalDetail),
        ("DurationMs", Milliseconds(duration)));

    /// <summary>La ejecución ha terminado tras cancelarse y completar las compensaciones.</summary>
    public static LogEvent Cancelled(ExecutionId id, TimeSpan duration) => Create(
        "ExecutionCancelled",
        id,
        LogLevelKind.Warning,
        ("Outcome", CloneOutcome.Cancelled.ToString()),
        ("DurationMs", Milliseconds(duration)));

    /// <summary>La ejecución ha fallado en una fase, con su código estable y el detalle técnico.</summary>
    public static LogEvent Failed(ExecutionId id, TimeSpan duration, ExecutionFailure failure) => Create(
        "ExecutionFailed",
        id,
        LogLevelKind.Error,
        ("Outcome", CloneOutcome.Failed.ToString()),
        ("FailureCode", failure.Code),
        ("Phase", failure.Phase.ToString()),
        ("TechnicalDetail", failure.TechnicalDetail),
        ("DurationMs", Milliseconds(duration)));

    /// <summary>El plan se invalida porque cambió el estado de los discos antes de ejecutarlo (FR-011).</summary>
    public static LogEvent PlanInvalidated(ExecutionId id, PlanId plan, string reason) => Create(
        "PlanInvalidated",
        id,
        LogLevelKind.Warning,
        ("PlanId", Text(plan.Value)),
        ("Reason", reason));

    /// <summary>
    /// Un paso de liberación de recursos ha fallado (montajes, instantánea o destino). No cambia el desenlace de la ejecución:
    /// <c>Step</c> indica cuál ha fallado.
    /// </summary>
    public static LogEvent CleanupFailed(ExecutionId id, string step, string technicalDetail) => Create(
        "CleanupFailed",
        id,
        LogLevelKind.Warning,
        ("Step", step),
        ("TechnicalDetail", technicalDetail));

    /// <summary>
    /// La restauración del tamaño original del origen ha fallado tras un clon verificado. No cambia el desenlace (el clon es
    /// válido y la ejecución termina como Completed con SourceRestored = false), pero la partición del origen queda reducida.
    /// </summary>
    public static LogEvent SourceRestoreFailed(ExecutionId id, PartitionGuid partition, ByteSize originalLength, string technicalDetail) => Create(
        "SourceRestoreFailed",
        id,
        LogLevelKind.Warning,
        ("Partition", Text(partition.Value)),
        ("OriginalLength", originalLength.Bytes),
        ("TechnicalDetail", technicalDetail));

    private static LogEvent Create(string name, ExecutionId id, LogLevelKind level, params (string Key, object? Value)[] entries)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal) { ["ExecutionId"] = Text(id.Value) };
        foreach (var (key, value) in entries)
        {
            properties[key] = value;
        }

        return new LogEvent(name, properties, level);
    }

    private static long Milliseconds(TimeSpan duration) => (long)Math.Round(duration.TotalMilliseconds, MidpointRounding.AwayFromZero);

    private static string Text(Guid value) => value.ToString("D", CultureInfo.InvariantCulture);
}
