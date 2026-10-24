using WinClonerX.Domain.Shared;

namespace WinClonerX.Application.Ports;

/// <summary>Abre particiones no NTFS (EFI, MSR y datos) para copiarlas en bruto.</summary>
public interface IRawPartitionReader
{
    /// <summary>Abre el tramo indicado del disco de origen como origen de bloques de solo lectura, para copia completa.</summary>
    IBlockSource Open(DiskId source, Extent extent);
}
