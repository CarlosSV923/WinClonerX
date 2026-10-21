using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Ports;

/// <summary>
/// Compares the used ranges of one source volume or partition with the bytes the copy wrote to the target (verification
/// completa, FR-022a). Implemented by the infrastructure; the orchestrator decides which ranges and where.
/// </summary>
public interface IVolumeComparer
{
    /// <summary>
    /// Reads every range of <paramref name="allocation"/> from <paramref name="source"/> and from <paramref name="target"/> at
    /// <c>targetOffset + range.Offset</c>, and counts the bytes that differ. Only the ranges in the allocation are compared.
    /// Reports cumulative bytes compared through <paramref name="bytesCompared"/>. A read error aborts the comparison with a
    /// <c>PlatformException</c> (SourceReadError, or the target code). A difference is not an error: it is in the result.
    /// </summary>
    Task<RangeComparison> CompareAsync(
        IBlockSource source,
        VolumeAllocation allocation,
        ITargetDisk target,
        long targetOffset,
        IProgress<long> bytesCompared,
        CancellationToken ct);
}
