using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Gives a block source the name it has in the log (<see cref="IDiskOperationLabel"/>). It adds no behavior: every call goes
/// to the inner source, and the offsets are the inner source's offsets.
/// </summary>
internal sealed class LabeledBlockSource : IBlockSource, IDiskOperationLabel
{
    private readonly IBlockSource _inner;

    internal LabeledBlockSource(IBlockSource inner, string diskOperationLabel)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentException.ThrowIfNullOrWhiteSpace(diskOperationLabel);

        _inner = inner;
        DiskOperationLabel = diskOperationLabel;
    }

    /// <inheritdoc />
    public string DiskOperationLabel { get; }

    /// <inheritdoc />
    public ByteSize Length => _inner.Length;

    /// <inheritdoc />
    public Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct) => _inner.GetAllocationAsync(ct);

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct) =>
        _inner.ReadAsync(offset, alignedBuffer, ct);

    /// <inheritdoc />
    public void Dispose() => _inner.Dispose();
}
