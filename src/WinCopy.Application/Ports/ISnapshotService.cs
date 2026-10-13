using WinCopy.Domain.Shared;

namespace WinCopy.Application.Ports;

/// <summary>Crea instantáneas coherentes de los volúmenes NTFS del origen mientras Windows sigue en uso (R1).</summary>
public interface ISnapshotService
{
    /// <summary>Crea una instantánea de los volúmenes indicados; el conjunto devuelto debe disponerse siempre para borrarla.</summary>
    Task<ISnapshotSet> CreateAsync(IReadOnlyList<PartitionGuid> ntfsVolumes, CancellationToken ct);
}
