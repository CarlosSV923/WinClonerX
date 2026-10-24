using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Application.Ports;

/// <summary>Verifica los chunks del destino contra los digests capturados durante la copia.</summary>
public interface ITargetVerifier
{
    Task<RangeComparison> VerifyAsync(
        ITargetDisk target,
        IReadOnlyList<ChunkDigest> digests,
        IProgress<long> bytesVerified,
        CancellationToken ct);
}
