using WinCopy.Application.Ports;
using WinCopy.Domain.Planning;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>
/// Fake de ITargetDisk en memoria. Registra cada operación en orden (<see cref="FakeBase.Calls"/>) y cada escritura con su
/// desplazamiento y longitud. Las escrituras dentro de <see cref="Storage"/> se conservan para releerlas; fuera de él no se guardan.
/// </summary>
public sealed class FakeTargetDisk : FakeBase, ITargetDisk
{
    /// <summary>Contenido del disco en memoria. Vacío por defecto.</summary>
    public byte[] Storage { get; set; } = [];

    /// <summary>Escrituras realizadas, en orden: desplazamiento y longitud de cada una.</summary>
    public List<(long Offset, int Length)> Writes { get; } = [];

    /// <summary>Diseños confirmados con CommitLayoutAsync, en orden.</summary>
    public List<TargetLayout> CommittedLayouts { get; } = [];

    /// <summary>Número de veces que se ha invocado DisposeAsync, también si falló.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Registra la operación y devuelve el resultado configurado.</summary>
    public Task LockAndTakeOfflineAsync(CancellationToken ct) => Complete(nameof(LockAndTakeOfflineAsync), ct);

    /// <summary>Registra la operación.</summary>
    public Task WipeBoundariesAsync(CancellationToken ct) => Complete(nameof(WipeBoundariesAsync), ct);

    /// <summary>Registra la escritura y, si cabe en Storage, la guarda.</summary>
    public ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct) =>
        new(Complete(nameof(WriteAsync), ct, () =>
        {
            Writes.Add((diskOffset, alignedBuffer.Length));
            CopyIn(diskOffset, alignedBuffer);
        }));

    /// <summary>Copia desde Storage al búfer y devuelve los bytes leídos (0 fuera del disco).</summary>
    public ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct) =>
        ReplyValue(nameof(ReadAsync), () => CopyOut(diskOffset, alignedBuffer), ct);

    /// <summary>Registra el diseño confirmado.</summary>
    public Task CommitLayoutAsync(TargetLayout layout, CancellationToken ct) =>
        Complete(nameof(CommitLayoutAsync), ct, () => CommittedLayouts.Add(layout));

    /// <summary>Registra la operación.</summary>
    public Task BringOnlineAsync(CancellationToken ct) => Complete(nameof(BringOnlineAsync), ct);

    /// <summary>Registra la compensación.</summary>
    public Task DeleteLayoutAsync(CancellationToken ct) => Complete(nameof(DeleteLayoutAsync), ct);

    /// <summary>Cuenta la liberación y la registra; el fallo configurado sale por la Task.</summary>
    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        return new ValueTask(Complete(nameof(DisposeAsync), CancellationToken.None));
    }

    private void CopyIn(long offset, ReadOnlyMemory<byte> data)
    {
        if (offset < 0 || offset + data.Length > Storage.Length)
        {
            return;
        }

        data.Span.CopyTo(Storage.AsSpan((int)offset));
    }

    private int CopyOut(long offset, Memory<byte> buffer)
    {
        if (offset < 0 || offset >= Storage.Length)
        {
            return 0;
        }

        var count = (int)Math.Min(buffer.Length, Storage.Length - offset);
        Storage.AsMemory((int)offset, count).CopyTo(buffer);

        return count;
    }
}
