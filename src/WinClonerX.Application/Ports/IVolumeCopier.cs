using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Application.Ports;

/// <summary>
/// Copies the allocated ranges of one source volume or partition to the target disk (research R3).
/// Implemented by the infrastructure copy pipeline; the orchestrator only decides what to copy and where.
/// </summary>
public interface IVolumeCopier
{
    /// <summary>
    /// Copies every range of <paramref name="allocation"/> from <paramref name="source"/> to
    /// <paramref name="target"/> at <c>targetOffset + range.Offset</c>. Reports cumulative bytes written
    /// through <paramref name="bytesWritten"/>. Any read or write error aborts the copy with a
    /// <c>PlatformException</c> (SourceReadError or TargetWriteError).
    /// </summary>
    Task CopyAsync(
        IBlockSource source,
        VolumeAllocation allocation,
        ITargetDisk target,
        long targetOffset,
        IProgress<long> bytesWritten,
        CancellationToken ct);
}
