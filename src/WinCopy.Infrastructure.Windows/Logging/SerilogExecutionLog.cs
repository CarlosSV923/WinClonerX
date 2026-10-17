using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using Serilog;
using Serilog.Core;
using Serilog.Formatting.Compact;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using LogEventLevel = Serilog.Events.LogEventLevel;

namespace WinCopy.Infrastructure.Windows.Logging;

/// <summary>
/// Operation log of one execution, written as CLEF (one JSON object per line) to
/// <c>%ProgramData%\WinCopy\logs\winCopy-yyyyMMdd-HHmmss-{executionId:N}.clef</c>.
/// </summary>
/// <remarks>
/// <para>Every event is flushed to disk as soon as it is recorded: the application may crash in the middle of a
/// clone and the log must survive it.</para>
/// <para>Every line carries <c>ExecutionId</c>, <c>AppVersion</c> and <c>Event</c>. Each property of the event becomes
/// a structured property, destructured with <c>@</c> so nested objects and arrays are written as JSON.</para>
/// <para>Property names <c>Event</c>, <c>ExecutionId</c> and <c>AppVersion</c> belong to the envelope and are rejected.</para>
/// </remarks>
internal sealed class SerilogExecutionLog : IExecutionLog, IDisposable
{
    private const string MessageTemplate = "{Event}";
    private const string InventoryEventName = "InventoryCaptured";
    private const string InventoryEntryName = "inventory.json";
    private const int CopyBufferSize = 81920;

    private static readonly JsonSerializerOptions InventoryJsonOptions = new() { WriteIndented = true };

    private readonly object _gate = new();
    private readonly Logger _sink;
    private readonly ILogger _context;
    private readonly string _currentFile;
    private IReadOnlyDictionary<string, object?>? _inventoryProperties;
    private bool _disposed;

    /// <summary>Creates the log under the default folder, <c>%ProgramData%\WinCopy\logs</c>.</summary>
    public static SerilogExecutionLog CreateDefault(Guid executionId, string appVersion, IClock clock)
    {
        string logDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "WinCopy", "logs");

        return new SerilogExecutionLog(logDirectory, executionId, appVersion, clock);
    }

    /// <summary>Creates the log for one execution inside <paramref name="logDirectory"/>, creating it if needed.</summary>
    public SerilogExecutionLog(string logDirectory, Guid executionId, string appVersion, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(logDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(appVersion);
        ArgumentNullException.ThrowIfNull(clock);

        Directory.CreateDirectory(logDirectory);

        string localStamp = clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        _currentFile = Path.Combine(logDirectory, $"winCopy-{localStamp}-{executionId:N}.clef");

        _sink = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.File(
                new CompactJsonFormatter(),
                _currentFile,
                fileSizeLimitBytes: null,
                buffered: false,
                shared: false,
                flushToDiskInterval: null)
            .CreateLogger();

        _context = _sink
            .ForContext("ExecutionId", executionId)
            .ForContext("AppVersion", appVersion);
    }

    /// <inheritdoc />
    public string CurrentFile => _currentFile;

    /// <inheritdoc />
    /// <remarks>Thread-safe. Writes are serialized, so the file keeps the order in which events were recorded.</remarks>
    public void Record(LogEvent e)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentException.ThrowIfNullOrWhiteSpace(e.Event);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            ILogger context = _context;
            foreach (KeyValuePair<string, object?> property in e.Properties)
            {
                if (LogEvent.ReservedPropertyNames.Contains(property.Key))
                {
                    throw new ArgumentException(
                        $"Property name '{property.Key}' is reserved by the log envelope.", nameof(e));
                }

                context = context.ForContext(property.Key, property.Value, destructureObjects: true);
            }

            context.Write(ToSerilogLevel(e.Level), MessageTemplate, e.Event);

            if (e.Event == InventoryEventName)
            {
                _inventoryProperties = new Dictionary<string, object?>(e.Properties, StringComparer.Ordinal);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The log file is read with <see cref="FileShare.ReadWrite"/>, so it can be exported while the execution is
    /// still writing to it. The archive holds only the <c>.clef</c> file and, when requested and an
    /// <c>InventoryCaptured</c> event was recorded, <c>inventory.json</c>. If the export fails or is cancelled,
    /// the partial archive is deleted.
    /// </remarks>
    public async Task ExportAsync(string destinationPath, bool includeInventory, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationPath);

        IReadOnlyDictionary<string, object?>? inventory = null;
        if (includeInventory)
        {
            lock (_gate)
            {
                inventory = _inventoryProperties;
            }
        }

        string? destinationDirectory = Path.GetDirectoryName(Path.GetFullPath(destinationPath));
        if (!string.IsNullOrEmpty(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        try
        {
            await WriteArchiveAsync(destinationPath, inventory, ct).ConfigureAwait(false);
        }
        catch
        {
            TryDeleteFile(destinationPath);
            throw;
        }
    }

    /// <summary>Closes the underlying file. Events recorded afterwards are rejected.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _sink.Dispose();
        }
    }

    private async Task WriteArchiveAsync(
        string destinationPath,
        IReadOnlyDictionary<string, object?>? inventory,
        CancellationToken ct)
    {
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            CopyBufferSize,
            useAsync: true);

        await using var archive = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

        await AddLogFileAsync(archive, ct).ConfigureAwait(false);

        if (inventory is not null)
        {
            await AddInventoryAsync(archive, inventory, ct).ConfigureAwait(false);
        }
    }

    private async Task AddLogFileAsync(ZipArchive archive, CancellationToken ct)
    {
        ZipArchiveEntry entry = archive.CreateEntry(Path.GetFileName(_currentFile), CompressionLevel.Optimal);

        await using var source = new FileStream(
            _currentFile,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite,
            CopyBufferSize,
            useAsync: true);
        await using var entryStream = entry.Open();

        await source.CopyToAsync(entryStream, ct).ConfigureAwait(false);
    }

    private static async Task AddInventoryAsync(
        ZipArchive archive,
        IReadOnlyDictionary<string, object?> inventory,
        CancellationToken ct)
    {
        ZipArchiveEntry entry = archive.CreateEntry(InventoryEntryName, CompressionLevel.Optimal);

        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, inventory, InventoryJsonOptions, ct).ConfigureAwait(false);
    }

    private static LogEventLevel ToSerilogLevel(LogLevelKind level) => level switch
    {
        LogLevelKind.Debug => LogEventLevel.Debug,
        LogLevelKind.Information => LogEventLevel.Information,
        LogLevelKind.Warning => LogEventLevel.Warning,
        LogLevelKind.Error => LogEventLevel.Error,
        _ => throw new ArgumentOutOfRangeException(nameof(level), level, "Unknown log level."),
    };

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the original failure is the one the caller needs to see.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: the original failure is the one the caller needs to see.
        }
    }
}
