using System.Globalization;

#pragma warning disable CA1716 // El nombre WinCopy.Domain.Shared lo fija la especificación del proyecto.
namespace WinCopy.Domain.Shared;
#pragma warning restore CA1716

/// <summary>Cantidad de bytes, siempre mayor o igual que cero, con formato en GiB y GB.</summary>
public readonly record struct ByteSize(long Bytes) : IComparable<ByteSize>
{
    private const long Kib = 1L << 10;
    private const long Mib = 1L << 20;
    private const long Gib = 1L << 30;

    private static readonly (long Factor, string Unit)[] BinaryUnits = [(Gib, "GiB"), (Mib, "MiB"), (Kib, "KiB")];
    private static readonly (long Factor, string Unit)[] DecimalUnits = [(1_000_000_000L, "GB"), (1_000_000L, "MB"), (1_000L, "KB")];

    /// <summary>Cantidad de bytes; rechaza valores negativos.</summary>
    public long Bytes { get; } = Bytes >= 0
        ? Bytes
        : throw new ArgumentOutOfRangeException(nameof(Bytes), Bytes, "La cantidad de bytes no puede ser negativa.");

    public static ByteSize operator +(ByteSize left, ByteSize right) => new(left.Bytes + right.Bytes);

    public static ByteSize operator -(ByteSize left, ByteSize right) => new(left.Bytes - right.Bytes);

    public static bool operator <(ByteSize left, ByteSize right) => left.Bytes < right.Bytes;

    public static bool operator >(ByteSize left, ByteSize right) => left.Bytes > right.Bytes;

    public static bool operator <=(ByteSize left, ByteSize right) => left.Bytes <= right.Bytes;

    public static bool operator >=(ByteSize left, ByteSize right) => left.Bytes >= right.Bytes;

    /// <summary>Crea una cantidad a partir de GiB (potencias de 1024).</summary>
    public static ByteSize FromGiB(double gib)
    {
        if (!double.IsFinite(gib))
        {
            throw new ArgumentOutOfRangeException(nameof(gib), gib, "La cantidad debe ser un número finito.");
        }

        return new ByteSize(checked((long)Math.Round(gib * Gib)));
    }

    /// <summary>Crea una cantidad a partir de MiB (potencias de 1024).</summary>
    public static ByteSize FromMiB(long mib) => new(checked(mib * Mib));

    /// <summary>Formatea con unidades binarias (GiB, MiB, KiB o B), independientemente de la cultura.</summary>
    public override string ToString() => Format(BinaryUnits);

    /// <summary>Formatea con unidades decimales (GB, MB, KB o B), independientemente de la cultura.</summary>
    public string ToDecimalString() => Format(DecimalUnits);

    public int CompareTo(ByteSize other) => Bytes.CompareTo(other.Bytes);

    private string Format((long Factor, string Unit)[] units)
    {
        foreach (var (factor, unit) in units)
        {
            if (Bytes >= factor)
            {
                return ((double)Bytes / factor).ToString("F2", CultureInfo.InvariantCulture) + " " + unit;
            }
        }

        return Bytes.ToString(CultureInfo.InvariantCulture) + " B";
    }
}
