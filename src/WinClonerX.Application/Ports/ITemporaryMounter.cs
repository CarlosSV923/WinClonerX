using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Ports;

/// <summary>Monta temporalmente un volumen para poder leerlo o escribirlo como archivos.</summary>
public interface ITemporaryMounter
{
    /// <summary>Monta la partición en un punto de montaje temporal; liberar el resultado lo retira.</summary>
    Task<TemporaryMount> MountAsync(PartitionGuid partition, CancellationToken ct);
}
