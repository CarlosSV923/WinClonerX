using WinClonerX.Application.Ports;

namespace WinClonerX.Infrastructure.Windows.Logging;

/// <summary>
/// Records <c>DiskOperation</c> events in the execution log (log-events.md).
/// </summary>
/// <remarks>
/// <para>
/// Operations are aggregated, not per buffer. The caller begins one scope per copy (one Read and one Write per partition)
/// or per high-level operation (Lock, Offline, Online, CommitLayout, DeleteLayout, Shrink, Restore, Extend), never per
/// range or per I/O buffer. Recording every buffer would flood the log and slow the copy down.
/// </para>
/// <para>
/// Allowed operation names: Read, Write, Lock, Offline, Online, CommitLayout, DeleteLayout, Shrink, Restore, Extend.
/// Any other name throws <see cref="ArgumentException"/>.
/// </para>
/// </remarks>
internal sealed class DiskOperationRecorder
{
    private static readonly HashSet<string> AllowedOperations = new(StringComparer.Ordinal)
    {
        "Read",
        "Write",
        "Lock",
        "Offline",
        "Online",
        "CommitLayout",
        "DeleteLayout",
        "Shrink",
        "Restore",
        "Extend",
    };

    private readonly IExecutionLog _log;
    private readonly IClock _clock;

    public DiskOperationRecorder(IExecutionLog log, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(log);
        ArgumentNullException.ThrowIfNull(clock);

        _log = log;
        _clock = clock;
    }

    /// <summary>
    /// Starts timing one operation on one disk range. Dispose the returned scope to emit the event.
    /// </summary>
    /// <param name="disk">Disk identifier as shown in the log, for example the disk GUID.</param>
    /// <param name="operation">One of the allowed operation names.</param>
    /// <param name="offset">Byte offset of the range, when it has one.</param>
    /// <param name="length">Byte length of the range, when it has one.</param>
    /// <exception cref="ArgumentException">The operation name is not allowed, or the disk is empty.</exception>
    public DiskOperationScope Begin(string disk, string operation, long? offset = null, long? length = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(disk);

        if (operation is null || !AllowedOperations.Contains(operation))
        {
            throw new ArgumentException($"'{operation}' is not an allowed disk operation.", nameof(operation));
        }

        return new DiskOperationScope(_log, _clock, disk, operation, offset, length);
    }
}
