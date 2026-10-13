namespace WinCopy.Domain.Shared;

/// <summary>Tramo contiguo de un disco, definido por su desplazamiento y su longitud en bytes.</summary>
public readonly record struct Extent(long Offset, long Length)
{
    /// <summary>Crea una extensión que cumple las reglas del tipo, exigiendo además que el desplazamiento esté alineado al sector.</summary>
    public static Extent Create(long offset, long length, SectorSize sector)
    {
        if (offset % sector.Bytes != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), offset,
                "El desplazamiento está alineado al sector y la longitud es > 0.");
        }

        return new Extent(offset, length);
    }

    /// <summary>Desplazamiento en bytes desde el inicio del disco; no puede ser negativo.</summary>
    public long Offset { get; } = Offset >= 0
        ? Offset
        : throw new ArgumentOutOfRangeException(nameof(Offset), Offset, "El desplazamiento no puede ser negativo.");

    /// <summary>Longitud en bytes; debe ser mayor que cero.</summary>
    public long Length { get; } = Length > 0
        ? Length
        : throw new ArgumentOutOfRangeException(nameof(Length), Length, "La longitud debe ser mayor que cero.");

    /// <summary>Posición de final exclusiva: Offset + Length.</summary>
    public long End => Offset + Length;

    /// <summary>Indica si el desplazamiento y la longitud son múltiplos del alineamiento indicado.</summary>
    public bool IsAlignedTo(long alignment)
    {
        if (alignment <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(alignment), alignment, "El alineamiento debe ser mayor que cero.");
        }

        return Offset % alignment == 0 && Length % alignment == 0;
    }

    /// <summary>Indica si esta extensión y otra comparten algún byte.</summary>
    public bool Overlaps(Extent other) => Offset < other.End && other.Offset < End;
}
