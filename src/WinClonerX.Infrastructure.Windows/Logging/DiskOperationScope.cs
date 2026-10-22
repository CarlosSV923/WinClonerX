using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Infrastructure.Windows.Logging;

/// <summary>
/// Times one disk operation and emits a single <c>DiskOperation</c> event when disposed.
/// </summary>
/// <remarks>
/// <para>Call <see cref="Succeed"/> or <see cref="Fail"/> once. The first call wins; later calls are ignored.</para>
/// <para>If the scope is disposed without either call, the result is <c>Abandoned</c>.</para>
/// </remarks>
internal sealed class DiskOperationScope : IDisposable
{
    private const string EventName = "DiskOperation";

    private readonly IExecutionLog _log;
    private readonly IClock _clock;
    private readonly string _disk;
    private readonly string _operation;
    private readonly long? _offset;
    private long? _length;
    private readonly DateTimeOffset _start;

    private string? _result;
    private Exception? _error;
    private bool _disposed;

    internal DiskOperationScope(
        IExecutionLog log,
        IClock clock,
        string disk,
        string operation,
        long? offset,
        long? length)
    {
        _log = log;
        _clock = clock;
        _disk = disk;
        _operation = operation;
        _offset = offset;
        _length = length;
        _start = clock.UtcNow;
    }

    /// <summary>Sets the byte length the event reports, for an operation whose length is known only when it ends.</summary>
    public void SetLength(long length)
    {
        _length = length;
    }

    /// <summary>Marks the operation as completed successfully.</summary>
    public void Succeed()
    {
        _result ??= "Success";
    }

    /// <summary>Marks the operation as failed with <paramref name="ex"/>.</summary>
    public void Fail(Exception ex)
    {
        ArgumentNullException.ThrowIfNull(ex);

        if (_result is not null)
        {
            return;
        }

        _result = "Failed";
        _error = ex;
    }

    /// <summary>Emits the <c>DiskOperation</c> event. Only the first call has an effect.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        bool failed = _result == "Failed";
        long durationMs = (long)(_clock.UtcNow - _start).TotalMilliseconds;

        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Disk"] = _disk,
            ["Operation"] = _operation,
            ["Offset"] = _offset,
            ["Length"] = _length,
            ["DurationMs"] = durationMs,
            ["Result"] = _result ?? "Abandoned",
        };

        if (failed && _error is not null)
        {
            properties["Error"] = new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Type"] = _error.GetType().FullName,
                ["Message"] = _error.Message,
            };
        }

        _log.Record(new LogEvent(
            EventName,
            properties,
            failed ? LogLevelKind.Error : LogLevelKind.Information));
    }
}
