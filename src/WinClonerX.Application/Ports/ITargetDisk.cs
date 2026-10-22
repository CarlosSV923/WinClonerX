using WinClonerX.Domain.Planning;

namespace WinClonerX.Application.Ports;

/// <summary>
/// Acceso de escritura y lectura en bruto al disco de destino (R4). Solo se obtiene con un <see cref="ClonePlan"/> confirmado,
/// mediante <see cref="ITargetDiskFactory"/>. Las escrituras y lecturas deben estar alineadas al sector.
/// Dispose libera los bloqueos y, si el disco sigue fuera de línea, lo vuelve a poner en línea. Un clon a medias ya
/// tiene la tabla de particiones borrada (compensación DeleteTargetLayout o límites borrados al prepararlo), así que
/// en línea se ve como un disco sin inicializar y nunca puede arrancar.
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
