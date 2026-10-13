using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Ports;

/// <summary>Origen de lectura por bloques alineados a sector sobre un volumen o una partición.</summary>
public interface IBlockSource : IDisposable
{
    /// <summary>Longitud total del origen en bytes.</summary>
    ByteSize Length { get; }

    /// <summary>Devuelve los tramos ocupados del volumen, relativos al volumen, incluidos el sector 0 y la región final (R2).</summary>
    Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct);

    /// <summary>Lee desde el desplazamiento indicado en el búfer alineado y devuelve el número de bytes leídos.</summary>
    ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct);
}
