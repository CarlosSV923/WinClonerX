using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Bytes que se copiarán y duración prevista del clonado. <see cref="EstimatedDuration"/> incluye la verificación completa si
/// está activada; <see cref="EstimatedCopyDuration"/> es solo la copia, que es la que sirve para el rendimiento inicial.
/// </summary>
public sealed record CloneEstimate(ByteSize BytesToCopy, TimeSpan EstimatedDuration, TimeSpan EstimatedCopyDuration)
{
    /// <summary>
    /// Calcula la estimación: los clústeres ocupados en las particiones NTFS y el tramo completo en el resto.
    /// La velocidad es la del bus más lento entre origen y destino. La verificación completa lee de nuevo ambos lados, así que
    /// se estima como otra copia completa (aprox. duplica la duración, FR-022a).
    /// </summary>
    public static CloneEstimate Calculate(
        IEnumerable<PartitionMapping> mappings,
        BusKind sourceBus,
        BusKind targetBus,
        VerificationMode mode = VerificationMode.Structural)
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
        var copy = TimeSpan.FromSeconds(bytes / (megabytesPerSecond * 1_000_000.0));
        var total = mode == VerificationMode.StructuralAndFull ? copy + copy : copy;

        return new CloneEstimate(new ByteSize(bytes), total, copy);
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
