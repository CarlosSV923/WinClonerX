using WinClonerX.Application.Ports;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de ISourceResizer con mínimo y máximo configurables y registro de reducciones y restauraciones.</summary>
public sealed class FakeSourceResizer : FakeBase, ISourceResizer
{
    /// <summary>Tamaño mínimo que devuelve GetMinimumSizeAsync.</summary>
    public ByteSize MinimumSize { get; set; } = ByteSize.FromGiB(10);

    /// <summary>Tamaño máximo que devuelve GetMaximumSizeAsync.</summary>
    public ByteSize MaximumSize { get; set; } = ByteSize.FromGiB(4096);

    /// <summary>Reducciones solicitadas, en orden.</summary>
    public List<(PartitionGuid Partition, ByteSize Length)> Shrinks { get; } = [];

    /// <summary>Restauraciones solicitadas, en orden.</summary>
    public List<(PartitionGuid Partition, ByteSize Length)> Restores { get; } = [];

    /// <summary>Particiones consultadas con GetMinimumSizeAsync, en orden.</summary>
    public List<PartitionGuid> MinimumQueries { get; } = [];

    /// <summary>
    /// Efecto de una restauración con éxito, para simular el disco: por ejemplo, actualizar el inventario que lee la
    /// verificación posterior. Se ejecuta después de registrar la llamada.
    /// </summary>
    public Action<PartitionGuid, ByteSize>? RestoreEffect { get; set; }

    /// <summary>Devuelve el mínimo configurado y registra la partición consultada.</summary>
    public Task<ByteSize> GetMinimumSizeAsync(PartitionGuid partition, CancellationToken ct) =>
        Reply(nameof(GetMinimumSizeAsync), () =>
        {
            MinimumQueries.Add(partition);
            return MinimumSize;
        }, ct);

    /// <summary>Devuelve el máximo configurado.</summary>
    public Task<ByteSize> GetMaximumSizeAsync(PartitionGuid partition, CancellationToken ct) =>
        Reply(nameof(GetMaximumSizeAsync), () => MaximumSize, ct);

    /// <summary>Registra la reducción solicitada.</summary>
    public Task ShrinkAsync(PartitionGuid partition, ByteSize newLength, CancellationToken ct) =>
        Complete(nameof(ShrinkAsync), ct, () => Shrinks.Add((partition, newLength)));

    /// <summary>Registra la restauración solicitada y aplica su efecto, si lo hay.</summary>
    public Task RestoreAsync(PartitionGuid partition, ByteSize originalLength, CancellationToken ct) =>
        Complete(nameof(RestoreAsync), ct, () =>
        {
            Restores.Add((partition, originalLength));
            RestoreEffect?.Invoke(partition, originalLength);
        });
}
