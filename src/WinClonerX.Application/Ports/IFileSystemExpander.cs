using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Ports;

/// <summary>Amplía el sistema de archivos de un volumen para ocupar toda su partición (FSCTL_EXTEND_VOLUME).</summary>
public interface IFileSystemExpander
{
    /// <summary>Amplía el volumen de la partición de destino hasta el final de la partición.</summary>
    Task ExpandToPartitionAsync(PartitionGuid targetPartition, CancellationToken ct);
}
