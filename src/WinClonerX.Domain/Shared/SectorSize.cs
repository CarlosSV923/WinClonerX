namespace WinClonerX.Domain.Shared;

/// <summary>Tamaño de sector lógico o físico de un disco. En la versión 1 solo vale 512 o 4096 bytes.</summary>
public readonly record struct SectorSize(int Bytes)
{
    /// <summary>Sector de 512 bytes.</summary>
    public static SectorSize Bytes512 => new(512);

    /// <summary>Sector de 4096 bytes.</summary>
    public static SectorSize Bytes4096 => new(4096);

    /// <summary>Tamaño en bytes; solo admite 512 o 4096.</summary>
    public int Bytes { get; } = (Bytes is 512 or 4096)
        ? Bytes
        : throw new ArgumentOutOfRangeException(nameof(Bytes), Bytes, "Vale 512 o 4096 en la v1.");
}
