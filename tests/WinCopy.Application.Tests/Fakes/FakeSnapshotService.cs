using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de ISnapshotService: cada CreateAsync produce un FakeSnapshotSet nuevo que expone VolumesToServe.</summary>
public sealed class FakeSnapshotService : FakeBase, ISnapshotService
{
    /// <summary>Volúmenes que expone cada instantánea creada, indexados por partición.</summary>
    public Dictionary<PartitionGuid, FakeBlockSource> VolumesToServe { get; } = [];

    /// <summary>Conjuntos de instantáneas creados, en orden.</summary>
    public List<FakeSnapshotSet> CreatedSets { get; } = [];

    /// <summary>Volúmenes pedidos en la última llamada a CreateAsync que tuvo éxito.</summary>
    public IReadOnlyList<PartitionGuid> LastRequestedVolumes { get; private set; } = [];

    /// <summary>Crea un conjunto de instantáneas que expone los volúmenes configurados.</summary>
    public Task<ISnapshotSet> CreateAsync(IReadOnlyList<PartitionGuid> ntfsVolumes, CancellationToken ct) =>
        Reply<ISnapshotSet>(nameof(CreateAsync), () =>
        {
            LastRequestedVolumes = ntfsVolumes.ToArray();
            var set = new FakeSnapshotSet(VolumesToServe);
            CreatedSets.Add(set);
            return set;
        }, ct);
}
