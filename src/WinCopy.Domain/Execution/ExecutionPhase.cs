namespace WinCopy.Domain.Execution;

/// <summary>
/// Fase de una ejecución de clonado, en el orden en que se ejecutan. <see cref="ShrinkSource"/>, <see cref="FullVerification"/>,
/// <see cref="ExpandFileSystem"/> y <see cref="RestoreSource"/> son condicionales. <see cref="Done"/> marca el final de una
/// ejecución completada.
/// </summary>
public enum ExecutionPhase
{
    /// <summary>Comprobar que el inventario sigue siendo el del plan (huella).</summary>
    Revalidate,

    /// <summary>Reducir temporalmente la partición de Windows del origen, si el destino es más pequeño.</summary>
    ShrinkSource,

    /// <summary>Crear la instantánea coherente de los volúmenes del origen.</summary>
    Snapshot,

    /// <summary>Bloquear, poner fuera de línea y limpiar el destino.</summary>
    PrepareTarget,

    /// <summary>Copiar los datos de cada partición a su posición en el destino.</summary>
    CopyData,

    /// <summary>
    /// Comparar byte a byte los tramos copiados con los del origen. Opcional. Se hace con el destino todavía fuera de línea y sin
    /// volúmenes montados, antes de escribir el diseño y antes de cualquier cambio posterior (ampliación, arranque).
    /// </summary>
    FullVerification,

    /// <summary>Escribir la tabla de particiones del destino: es el último paso que hace visible el diseño.</summary>
    CommitLayout,

    /// <summary>Ampliar el sistema de archivos de Windows al espacio adicional del destino.</summary>
    ExpandFileSystem,

    /// <summary>Ajustar el clon para que arranque: MountedDevices, BCD y WinRE.</summary>
    RepairBoot,

    /// <summary>Comprobar la estructura del clon.</summary>
    StructuralVerification,

    /// <summary>Borrar la instantánea y retirar los montajes temporales.</summary>
    Cleanup,

    /// <summary>Restaurar el tamaño del origen si se redujo.</summary>
    RestoreSource,

    /// <summary>Ejecución completada.</summary>
    Done,
}
