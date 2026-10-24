using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Ports.Models;

/// <summary>Mapa de ocupación de un volumen: tamaño de clúster y tramos ocupados, relativos al volumen.</summary>
public sealed record VolumeAllocation(ByteSize ClusterSize, IReadOnlyList<Extent> UsedRanges)
{
    /// <summary>Suma de la longitud de todos los tramos ocupados.</summary>
    public ByteSize TotalUsed => new(UsedRanges.Sum(range => range.Length));
}
