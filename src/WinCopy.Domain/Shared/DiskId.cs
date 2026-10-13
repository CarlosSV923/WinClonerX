namespace WinCopy.Domain.Shared;

/// <summary>Identificador de disco: el número del sistema y el GUID de disco. La identidad es el GUID.</summary>
public readonly struct DiskId : IEquatable<DiskId>
{
    /// <summary>Crea un identificador de disco.</summary>
    public DiskId(int number, Guid diskGuid)
    {
        Number = number;
        DiskGuid = diskGuid;
    }

    /// <summary>Número de disco asignado por el sistema; solo se usa para las APIs.</summary>
    public int Number { get; }

    /// <summary>GUID de disco; es la identidad del disco.</summary>
    public Guid DiskGuid { get; }

    /// <summary>Dos identificadores son iguales si tienen el mismo GUID de disco, sin importar el número.</summary>
    public bool Equals(DiskId other) => DiskGuid == other.DiskGuid;

    public override bool Equals(object? obj) => obj is DiskId other && Equals(other);

    public override int GetHashCode() => DiskGuid.GetHashCode();

    public static bool operator ==(DiskId left, DiskId right) => left.Equals(right);

    public static bool operator !=(DiskId left, DiskId right) => !left.Equals(right);
}
