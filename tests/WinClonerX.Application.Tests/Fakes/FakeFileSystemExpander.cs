using WinClonerX.Application.Ports;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de IFileSystemExpander que registra las particiones ampliadas.</summary>
public sealed class FakeFileSystemExpander : FakeBase, IFileSystemExpander
{
    /// <summary>Particiones ampliadas, en orden.</summary>
    public List<PartitionGuid> Expanded { get; } = [];

    /// <summary>Registra la partición ampliada.</summary>
    public Task ExpandToPartitionAsync(PartitionGuid targetPartition, CancellationToken ct) =>
        Complete(nameof(ExpandToPartitionAsync), ct, () => Expanded.Add(targetPartition));
}
