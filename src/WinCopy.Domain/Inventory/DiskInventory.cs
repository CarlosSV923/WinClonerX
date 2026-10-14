using System.Globalization;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Inventory;

/// <summary>Imagen del estado de todos los discos en un instante, usada para planificar y detectar cambios.</summary>
public sealed record DiskInventory(DateTimeOffset CapturedAt, FirmwareType Firmware, IReadOnlyList<Disk> Disks)
{
    private int SystemDiskCount => Disks.Count(disk => disk.IsSystem);

    /// <summary>Indica si el inventario es soportado, es decir, si tiene exactamente un disco del sistema.</summary>
    public bool IsSupported => SystemDiskCount == 1;

    /// <summary>Motivo por el que el inventario no es soportado, o null si lo es.</summary>
    public string? UnsupportedReason => SystemDiskCount switch
    {
        1 => null,
        0 => "NoSystemDisk",
        _ => "MultipleSystemDisks",
    };

    /// <summary>Disco del sistema, o null si el inventario no es soportado.</summary>
    public Disk? SystemDisk => IsSupported ? Disks.First(disk => disk.IsSystem) : null;

    /// <summary>
    /// Busca un disco por su identificador, con la igualdad de <see cref="DiskId"/>: por GUID de disco y, en discos
    /// sin GPT (GUID vacío), por número de disco.
    /// </summary>
    public Disk? FindDisk(DiskId id) => Disks.FirstOrDefault(disk => disk.Id == id);

    /// <summary>Calcula la huella de identidad y diseño de los discos de origen y destino.</summary>
    public InventoryFingerprint Fingerprint(DiskId source, DiskId target)
    {
        var sourceDisk = FindDisk(source)
            ?? throw new ArgumentException("El disco de origen no está en el inventario.", nameof(source));
        var targetDisk = FindDisk(target)
            ?? throw new ArgumentException("El disco de destino no está en el inventario.", nameof(target));

        return InventoryFingerprint.Compute(CanonicalParts(sourceDisk).Concat(CanonicalParts(targetDisk)));
    }

    private static IEnumerable<string> CanonicalParts(Disk disk)
    {
        yield return disk.Id.DiskGuid.ToString("D", CultureInfo.InvariantCulture);
        yield return disk.SerialNumber;
        yield return disk.Size.Bytes.ToString(CultureInfo.InvariantCulture);

        foreach (var partition in disk.Partitions)
        {
            yield return partition.Number.ToString(CultureInfo.InvariantCulture);
            yield return partition.Extent.Offset.ToString(CultureInfo.InvariantCulture);
            yield return partition.Extent.Length.ToString(CultureInfo.InvariantCulture);
            yield return partition.Type.Value.ToString("D", CultureInfo.InvariantCulture);
            yield return partition.UniqueId.Value.ToString("D", CultureInfo.InvariantCulture);
        }
    }
}
