namespace WinCopy.Domain.Execution;

/// <summary>Hecho ocurrido durante una ejecución. Se registra en el log y alimenta la interfaz.</summary>
public abstract record ExecutionEvent(ExecutionId Id, DateTimeOffset At);

/// <summary>La ejecución ha empezado.</summary>
public sealed record ExecutionStarted(ExecutionId Id, DateTimeOffset At) : ExecutionEvent(Id, At);

/// <summary>Ha empezado una fase.</summary>
public sealed record PhaseStarted(ExecutionId Id, DateTimeOffset At, ExecutionPhase Phase) : ExecutionEvent(Id, At);

/// <summary>Ha terminado una fase, tras la duración indicada.</summary>
public sealed record PhaseCompleted(ExecutionId Id, DateTimeOffset At, ExecutionPhase Phase, TimeSpan Duration) : ExecutionEvent(Id, At);

/// <summary>Se ha medido un nuevo avance de la copia o de la verificación completa.</summary>
public sealed record ProgressReported(ExecutionId Id, DateTimeOffset At, CloneProgress Progress) : ExecutionEvent(Id, At);

/// <summary>Una compensación ha terminado, con o sin éxito.</summary>
public sealed record CompensationCompleted(ExecutionId Id, DateTimeOffset At, CompensationKind Kind, bool Success, string? Detail)
    : ExecutionEvent(Id, At);

/// <summary>La ejecución ha completado todas sus fases.</summary>
public sealed record ExecutionCompleted(ExecutionId Id, DateTimeOffset At) : ExecutionEvent(Id, At);

/// <summary>La ejecución ha terminado tras cancelarse.</summary>
public sealed record ExecutionCancelled(ExecutionId Id, DateTimeOffset At) : ExecutionEvent(Id, At);

/// <summary>La ejecución ha terminado con un fallo.</summary>
public sealed record ExecutionFailed(ExecutionId Id, DateTimeOffset At, ExecutionFailure Failure) : ExecutionEvent(Id, At);
