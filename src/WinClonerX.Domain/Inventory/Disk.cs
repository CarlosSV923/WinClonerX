using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Inventory;

/// <summary>Dispositivo de almacenamiento físico con sus particiones ordenadas por desplazamiento.</summary>
public sealed record Disk(
    DiskId Id,
    string Model,
    string SerialNumber,
    ByteSize Size,
    SectorSize LogicalSectorSize,
    SectorSize PhysicalSectorSize,
    BusKind Bus,
    PartitionStyle PartitionStyle,
    bool IsSystem,
    IReadOnlyList<Partition> Partitions,
    bool IsOffline,
    bool IsReadOnly)
{
    /// <summary>Particiones ordenadas por desplazamiento.</summary>
    public IReadOnlyList<Partition> Partitions { get; } = Partitions is null
        ? throw new ArgumentNullException(nameof(Partitions))
        : Partitions.OrderBy(partition => partition.Extent.Offset).ToArray();
}
