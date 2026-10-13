using WinCopy.Domain.Shared;

namespace WinCopy.Application.Ports;

/// <summary>Conjunto de instantáneas de una ejecución. Disponerlo borra las instantáneas, siempre.</summary>
public interface ISnapshotSet : IAsyncDisposable
{
    /// <summary>Abre el volumen de la instantánea como origen de bloques de solo lectura.</summary>
    IBlockSource OpenVolume(PartitionGuid partition);
}
