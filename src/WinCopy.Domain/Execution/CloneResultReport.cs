namespace WinCopy.Domain.Execution;

/// <summary>Desenlace final de una ejecución.</summary>
public enum CloneOutcome
{
    /// <summary>Todas las fases completadas.</summary>
    Completed,

    /// <summary>Cancelada por la persona.</summary>
    Cancelled,

    /// <summary>Fallida en alguna fase.</summary>
    Failed,
}

/// <summary>Estado del entorno de recuperación (WinRE) del clon. <c>Warning</c> no impide arrancar.</summary>
public enum RecoveryEnvironmentState
{
    /// <summary>WinRE apunta a la partición de recuperación del clon.</summary>
    Configured,

    /// <summary>WinRE no se ha podido configurar; hay que activarlo a mano.</summary>
    Warning,
}

/// <summary>Acción que la persona debe hacer después del clonado.</summary>
public enum NextStepCode
{
    /// <summary>Apagar el equipo y sustituir el disco original por el clon.</summary>
    ShutDownAndReplaceDisk,

    /// <summary>Elegir el disco de arranque en la configuración del firmware.</summary>
    SelectBootDeviceInFirmware,

    /// <summary>Volver a activar BitLocker en el clon.</summary>
    ReenableBitLocker,

    /// <summary>Conservar el disco original hasta comprobar que el clon arranca.</summary>
    KeepOriginalDiskUntilVerified,
}

/// <summary>
/// Informe final de una ejecución: desenlace, verificaciones, estado del origen y pasos siguientes. <paramref name="Failure"/>
/// es el motivo del fallo: solo tiene valor cuando el desenlace es <see cref="CloneOutcome.Failed"/>; en un éxito o una
/// cancelación es nulo.
/// </summary>
public sealed record CloneResultReport(
    CloneOutcome Outcome,
    VerificationResult? Structural,
    VerificationResult? Full,
    bool? SourceRestored,
    RecoveryEnvironmentState? RecoveryEnvironment,
    TimeSpan Duration,
    string LogFile,
    IReadOnlyList<NextStepCode> NextSteps,
    ExecutionFailure? Failure = null);
