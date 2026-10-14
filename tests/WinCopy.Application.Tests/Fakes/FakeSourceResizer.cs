using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de ISourceResizer con mínimo configurable y registro de reducciones y restauraciones.</summary>
public sealed class FakeSourceResizer : FakeBase, ISourceResizer
{
    /// <summary>Tamaño mínimo que devuelve GetMinimumSizeAsync.</summary>
    public ByteSize MinimumSize { get; set; } = ByteSize.FromGiB(10);

    /// <summary>Reducciones solicitadas, en orden.</summary>
    public List<(PartitionGuid Partition, ByteSize Length)> Shrinks { get; } = [];

    /// <summary>Restauraciones solicitadas, en orden.</summary>
    public List<(PartitionGuid Partition, ByteSize Length)> Restores { get; } = [];

    /// <summary>Particiones consultadas con GetMinimumSizeAsync, en orden.</summary>
    public List<PartitionGuid> MinimumQueries { get; } = [];

    /// <summary>Devuelve el mínimo configurado y registra la partición consultada.</summary>
    public Task<ByteSize> GetMinimumSizeAsync(PartitionGuid partition, CancellationToken ct) =>
        Reply(nameof(GetMinimumSizeAsync), () =>
        {
            MinimumQueries.Add(partition);
            return MinimumSize;
        }, ct);

    /// <summary>Registra la reducción solicitada.</summary>
    public Task ShrinkAsync(PartitionGuid partition, ByteSize newLength, CancellationToken ct) =>
        Complete(nameof(ShrinkAsync), ct, () => Shrinks.Add((partition, newLength)));

    /// <summary>Registra la restauración solicitada.</summary>
    public Task RestoreAsync(PartitionGuid partition, ByteSize originalLength, CancellationToken ct) =>
        Complete(nameof(RestoreAsync), ct, () => Restores.Add((partition, originalLength)));
}
