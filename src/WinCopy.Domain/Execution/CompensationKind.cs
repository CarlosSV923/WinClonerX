namespace WinCopy.Domain.Execution;

/// <summary>
/// Acción que deshace un efecto parcial de una ejecución fallida o cancelada. El orden de los valores es el orden de
/// ejecución; <see cref="CloneExecution.RequiredCompensations"/> respeta ese orden.
/// </summary>
public enum CompensationKind
{
    /// <summary>Borrar el diseño del destino para que no pueda arrancar de un clon incompleto.</summary>
    DeleteTargetLayout,

    /// <summary>Borrar la instantánea.</summary>
    DeleteSnapshot,

    /// <summary>Retirar los montajes temporales.</summary>
    ReleaseMounts,

    /// <summary>Restaurar el tamaño del origen si se redujo. Solo en la versión 2 (US2).</summary>
    RestoreSource,

    /// <summary>Permitir de nuevo la suspensión del equipo.</summary>
    AllowSleep,
}
