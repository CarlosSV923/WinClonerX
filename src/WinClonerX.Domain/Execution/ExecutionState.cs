namespace WinClonerX.Domain.Execution;

/// <summary>Situación de una ejecución de clonado.</summary>
public enum ExecutionState
{
    /// <summary>Creada pero aún no iniciada. Una ejecución solo existe a partir de <see cref="CloneExecution.Start"/>.</summary>
    Pending,

    /// <summary>En marcha: avanza por sus fases.</summary>
    Running,

    /// <summary>Cancelación solicitada: ejecuta las compensaciones y termina como cancelada.</summary>
    Cancelling,

    /// <summary>Cancelada, con las compensaciones hechas.</summary>
    Cancelled,

    /// <summary>Fallo en una fase: ejecuta las compensaciones y termina como fallida.</summary>
    Failing,

    /// <summary>Fallida, con las compensaciones hechas.</summary>
    Failed,

    /// <summary>Todas las fases completadas.</summary>
    Completed,
}
