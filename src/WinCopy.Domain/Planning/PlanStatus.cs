namespace WinCopy.Domain.Planning;

/// <summary>Situación de un plan de clonado.</summary>
public enum PlanStatus
{
    /// <summary>En revisión: se puede aceptar avisos y confirmar.</summary>
    Draft,

    /// <summary>Confirmado: listo para ejecutar.</summary>
    Confirmed,

    /// <summary>Invalidado porque cambiaron los discos: hay que volver a planificar.</summary>
    Invalidated,
}
