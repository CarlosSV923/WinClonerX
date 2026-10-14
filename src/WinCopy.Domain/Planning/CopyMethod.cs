namespace WinCopy.Domain.Planning;

/// <summary>Forma de copiar una partición al destino.</summary>
public enum CopyMethod
{
    /// <summary>Solo los clústeres ocupados de una instantánea (volúmenes NTFS).</summary>
    UsedClustersFromSnapshot,

    /// <summary>Copia completa del tramo (EFI, MSR y el resto).</summary>
    RawFull,
}
