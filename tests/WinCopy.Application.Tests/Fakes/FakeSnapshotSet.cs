using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de ISnapshotSet que registra si se ha borrado (DisposeAsync llamado).</summary>
public sealed class FakeSnapshotSet : FakeBase, ISnapshotSet
{
    private readonly Dictionary<PartitionGuid, FakeBlockSource> _volumes;

    /// <summary>Crea un conjunto que expone los volúmenes indicados.</summary>
    public FakeSnapshotSet(IReadOnlyDictionary<PartitionGuid, FakeBlockSource> volumes)
    {
        _volumes = new Dictionary<PartitionGuid, FakeBlockSource>(volumes);
    }

    /// <summary>Indica si se ha llamado a DisposeAsync. Cuenta aunque la llamada falle, porque el borrado se intenta siempre.</summary>
    public bool Deleted { get; private set; }

    /// <summary>Número de veces que se ha llamado a DisposeAsync.</summary>
    public int DisposeCount { get; private set; }

    /// <summary>Devuelve el origen de bloques del volumen indicado.</summary>
    public IBlockSource OpenVolume(PartitionGuid partition)
    {
        Enter(nameof(OpenVolume));
        return _volumes[partition];
    }

    /// <summary>Marca la instantánea como borrada y devuelve el fallo configurado, si lo hay.</summary>
    public ValueTask DisposeAsync()
    {
        Deleted = true;
        DisposeCount++;
        return new ValueTask(Complete(nameof(DisposeAsync), CancellationToken.None));
    }
}
