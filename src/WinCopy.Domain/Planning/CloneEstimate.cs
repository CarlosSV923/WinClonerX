using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>Bytes que se copiarán y duración prevista del clonado.</summary>
public sealed record CloneEstimate(ByteSize BytesToCopy, TimeSpan EstimatedDuration)
{
    /// <summary>
    /// Calcula la estimación: los clústeres ocupados en las particiones NTFS y el tramo completo en el resto.
    /// La velocidad es la del bus más lento entre origen y destino.
    /// </summary>
    public static CloneEstimate Calculate(IEnumerable<PartitionMapping> mappings, BusKind sourceBus, BusKind targetBus)
    {
        ArgumentNullException.ThrowIfNull(mappings);

        var bytes = 0L;
        foreach (var mapping in mappings)
        {
            bytes += mapping.CopyMethod == CopyMethod.UsedClustersFromSnapshot && mapping.Source.Volume is { } volume
                ? volume.Used.Bytes
                : mapping.Source.Extent.Length;
        }

        var megabytesPerSecond = Math.Min(MegabytesPerSecond(sourceBus), MegabytesPerSecond(targetBus));
        return new CloneEstimate(new ByteSize(bytes), TimeSpan.FromSeconds(bytes / (megabytesPerSecond * 1_000_000.0)));
    }

    /// <summary>Rendimiento conservador por tipo de bus, en MB/s (decimales).</summary>
    private static int MegabytesPerSecond(BusKind bus) => bus switch
    {
        BusKind.Nvme => 1000,
        BusKind.Sata or BusKind.Sas => 350,
        BusKind.Usb => 120,
        BusKind.FileBackedVirtual => 200,
        _ => 100,
    };
}
