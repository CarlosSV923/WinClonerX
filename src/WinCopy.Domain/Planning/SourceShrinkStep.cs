using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Reducción temporal de la partición de Windows del origen antes del clonado (FR-021). Guarda la longitud original, la
/// longitud reducida (lo ocupado más el margen, redondeado a 1 MiB o el mínimo que admite Windows si es mayor) y el mínimo
/// que admite Windows. Invariantes: la longitud reducida es menor que la original, no baja del mínimo y es múltiplo de 1 MiB.
/// </summary>
public sealed record SourceShrinkStep
{
    private const long AlignmentBytes = 1024L * 1024;

    /// <summary>Crea el paso validando sus invariantes. Lanza <see cref="ArgumentOutOfRangeException"/> si alguna no se cumple.</summary>
    public SourceShrinkStep(PartitionGuid partition, ByteSize originalLength, ByteSize shrunkLength, ByteSize minimumSupported)
    {
        if (shrunkLength >= originalLength)
        {
            throw new ArgumentOutOfRangeException(nameof(shrunkLength), shrunkLength, "La longitud reducida debe ser menor que la original.");
        }

        if (shrunkLength < minimumSupported)
        {
            throw new ArgumentOutOfRangeException(nameof(shrunkLength), shrunkLength, "La longitud reducida no puede ser menor que el mínimo que admite Windows.");
        }

        if (shrunkLength.Bytes % AlignmentBytes != 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shrunkLength), shrunkLength, "La longitud reducida debe ser múltiplo de 1 MiB.");
        }

        Partition = partition;
        OriginalLength = originalLength;
        ShrunkLength = shrunkLength;
        MinimumSupported = minimumSupported;
    }

    /// <summary>Partición de Windows del origen que se reduce.</summary>
    public PartitionGuid Partition { get; }

    /// <summary>Longitud de la partición antes de reducirla.</summary>
    public ByteSize OriginalLength { get; }

    /// <summary>Longitud a la que se reduce temporalmente la partición.</summary>
    public ByteSize ShrunkLength { get; }

    /// <summary>Mínimo que Windows admite para su partición (<c>MSFT_Partition.GetSupportedSize</c>).</summary>
    public ByteSize MinimumSupported { get; }
}
