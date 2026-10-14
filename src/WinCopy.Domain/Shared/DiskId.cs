namespace WinCopy.Domain.Shared;

/// <summary>
/// Identificador de disco: el número del sistema y el GUID de disco.
/// <para>
/// Regla de igualdad: con GUID, la identidad es el GUID y el número se ignora (puede cambiar entre sesiones).
/// Los discos sin GUID de disco (RAW o MBR) tienen <see cref="Guid.Empty"/>; dos de ellos son iguales solo si
/// tienen el mismo número. Un identificador con GUID nunca es igual a uno sin GUID.
/// </para>
/// <para>
/// Motivo: si todos los discos sin GPT compartieran identidad, <c>DiskInventory.FindDisk</c> podría devolver
/// un disco distinto del elegido como destino y borrarlo. La revalidación de la huella del inventario (serie,
/// tamaño y diseño) protege contra la renumeración de discos entre el plan y la ejecución.
/// </para>
/// </summary>
public readonly struct DiskId : IEquatable<DiskId>
{
    /// <summary>Crea un identificador de disco.</summary>
    public DiskId(int number, Guid diskGuid)
    {
        Number = number;
        DiskGuid = diskGuid;
    }

    /// <summary>Número de disco asignado por el sistema; solo se usa para las APIs y para distinguir discos sin GUID.</summary>
    public int Number { get; }

    /// <summary>GUID de disco; es la identidad del disco cuando no está vacío. <see cref="Guid.Empty"/> en discos sin GPT.</summary>
    public Guid DiskGuid { get; }

    /// <summary>
    /// Dos identificadores son iguales si ambos tienen GUID y los GUID coinciden (sin importar el número),
    /// o si ambos carecen de GUID y tienen el mismo número. Si solo uno carece de GUID, no son iguales.
    /// </summary>
    public bool Equals(DiskId other)
    {
        bool thisHasGuid = DiskGuid != Guid.Empty;
        bool otherHasGuid = other.DiskGuid != Guid.Empty;

        if (thisHasGuid && otherHasGuid)
        {
            return DiskGuid == other.DiskGuid;
        }

        if (!thisHasGuid && !otherHasGuid)
        {
            return Number == other.Number;
        }

        return false;
    }

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is DiskId other && Equals(other);

    /// <summary>Coherente con <see cref="Equals(DiskId)"/>: con GUID se usa el GUID; sin GUID, el número.</summary>
    public override int GetHashCode() =>
        DiskGuid != Guid.Empty
            ? DiskGuid.GetHashCode()
            : HashCode.Combine(Number, NoGuidSalt);

    /// <summary>Igualdad según <see cref="Equals(DiskId)"/>.</summary>
    public static bool operator ==(DiskId left, DiskId right) => left.Equals(right);

    /// <summary>Desigualdad según <see cref="Equals(DiskId)"/>.</summary>
    public static bool operator !=(DiskId left, DiskId right) => !left.Equals(right);

    private const int NoGuidSalt = 0x4E6F4755;
}
