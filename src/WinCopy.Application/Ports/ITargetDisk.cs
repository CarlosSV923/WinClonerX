using WinCopy.Domain.Planning;

namespace WinCopy.Application.Ports;

/// <summary>
/// Acceso de escritura y lectura en bruto al disco de destino (R4). Solo se obtiene con un <see cref="ClonePlan"/> confirmado,
/// mediante <see cref="ITargetDiskFactory"/>. Las escrituras y lecturas deben estar alineadas al sector.
/// Dispose libera el disco y lo deja en línea solo si el compromiso se completó.
/// </summary>
public interface ITargetDisk : IAsyncDisposable
{
    /// <summary>Bloquea y desmonta los volúmenes del destino y lo pone fuera de línea de forma no persistente. Falla si está en uso.</summary>
    Task LockAndTakeOfflineAsync(CancellationToken ct);

    /// <summary>Borra los primeros y los últimos MiB del disco, invalidando el MBR y las GPT anteriores.</summary>
    Task WipeBoundariesAsync(CancellationToken ct);

    /// <summary>Escribe el búfer alineado en el desplazamiento absoluto indicado del disco.</summary>
    ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct);

    /// <summary>Lee desde el desplazamiento absoluto indicado en el búfer alineado y devuelve los bytes leídos (verificación).</summary>
    ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct);

    /// <summary>Compromiso: escribe el MBR protector y las GPT primaria y de respaldo con el diseño indicado.</summary>
    Task CommitLayoutAsync(TargetLayout layout, CancellationToken ct);

    /// <summary>Vuelve a poner el disco en línea y pide a Windows que vuelva a leer su diseño.</summary>
    Task BringOnlineAsync(CancellationToken ct);

    /// <summary>Compensación: borra la tabla de particiones del destino para que no pueda arrancar un clon incompleto.</summary>
    Task DeleteLayoutAsync(CancellationToken ct);
}
