namespace WinClonerX.Domain.Planning;

/// <summary>Ajuste de tamaño de una partición en el destino.</summary>
public enum ResizeKind
{
    /// <summary>Sin cambios: la partición conserva su tamaño.</summary>
    None,

    /// <summary>Ampliar el sistema de archivos al espacio adicional (solo la partición de Windows, si el destino es mayor).</summary>
    GrowFileSystem,
}
