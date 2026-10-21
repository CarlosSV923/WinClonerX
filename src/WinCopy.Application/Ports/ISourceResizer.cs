using WinCopy.Domain.Shared;

namespace WinCopy.Application.Ports;

/// <summary>Reduce y restaura la partición de Windows del origen. Solo se usa sobre la partición de Windows del origen (R6).</summary>
public interface ISourceResizer
{
    /// <summary>Devuelve el tamaño mínimo al que Windows permite reducir la partición (MSFT_Partition.GetSupportedSize).</summary>
    Task<ByteSize> GetMinimumSizeAsync(PartitionGuid partition, CancellationToken ct);

    /// <summary>
    /// Devuelve el tamaño máximo al que Windows permite crecer la partición (MSFT_Partition.GetSupportedSize, SizeMax). Lo usa la
    /// recuperación al iniciarse para no intentar una restauración que el disco no puede cumplir.
    /// </summary>
    Task<ByteSize> GetMaximumSizeAsync(PartitionGuid partition, CancellationToken ct);

    /// <summary>Reduce la partición a la longitud indicada; solo tras la confirmación explícita de reducción.</summary>
    Task ShrinkAsync(PartitionGuid partition, ByteSize newLength, CancellationToken ct);

    /// <summary>Restaura la partición a su longitud original; es un paso de compensación obligatorio (FR-021a).</summary>
    Task RestoreAsync(PartitionGuid partition, ByteSize originalLength, CancellationToken ct);
}
