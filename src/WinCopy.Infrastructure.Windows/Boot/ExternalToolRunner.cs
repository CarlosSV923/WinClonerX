using System.ComponentModel;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>Captured result of one tool run.</summary>
internal sealed record ToolResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Starts a process by absolute path, with an argument list, and captures its output. Tests fake it.</summary>
internal interface IToolProcessLauncher
{
    /// <summary>Runs <paramref name="absolutePath"/> with exactly <paramref name="arguments"/>. Throws <see cref="Win32Exception"/> if it cannot start.</summary>
    Task<ToolResult> RunAsync(string absolutePath, IReadOnlyList<string> arguments, CancellationToken ct);
}

/// <summary>
/// Runs the two Windows boot tools that the clone needs (bcdboot and reagentc), and nothing else (R7, T069).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only files named exactly <c>bcdboot.exe</c> or <c>reagentc.exe</c> run, and only from the System32 folder,
/// by absolute path. Names with a path separator, a colon or <c>..</c> are rejected.</item>
/// <item>Arguments are passed as an argument list, never as a joined string, with no shell.</item>
/// <item>Each run is recorded as an <c>ExternalToolRun</c> event (log-events.md). A non-zero exit code is logged as a
/// warning and returned to the caller, which decides whether it is a failure.</item>
/// </list>
/// </remarks>
internal sealed class ExternalToolRunner
{
    private const string EventName = "ExternalToolRun";

    private static readonly Dictionary<string, ToolSpec> Allowlist = new(StringComparer.Ordinal)
    {
        ["bcdboot.exe"] = new ToolSpec("bcdboot", PlatformErrorCode.BootConfigFailed),
        ["reagentc.exe"] = new ToolSpec("reagentc", PlatformErrorCode.RecoveryConfigFailed),
    };

    private readonly IToolProcessLauncher _launcher;
    private readonly IExecutionLog _log;
    private readonly string _systemDirectory;

    internal ExternalToolRunner(IToolProcessLauncher launcher, IExecutionLog log, string systemDirectory)
    {
        ArgumentNullException.ThrowIfNull(launcher);
        ArgumentNullException.ThrowIfNull(log);
        ArgumentException.ThrowIfNullOrWhiteSpace(systemDirectory);

        _launcher = launcher;
        _log = log;
        _systemDirectory = systemDirectory;
    }

    /// <summary>Production instance: the real launcher and the System32 folder of this machine.</summary>
    internal static ExternalToolRunner ForThisMachine(IExecutionLog log) =>
        new(new ProcessToolLauncher(), log, Environment.GetFolderPath(Environment.SpecialFolder.System));

    /// <summary>
    /// Runs an allowed tool from System32 with the given arguments and returns its exit code and output.
    /// Throws <see cref="ArgumentException"/> for a name that is not allowed, and <see cref="PlatformException"/> if the
    /// process cannot start.
    /// </summary>
    public async Task<ToolResult> RunAsync(string toolFileName, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(arguments);

        ToolSpec tool = Resolve(toolFileName);
        string absolutePath = Path.Combine(_systemDirectory, toolFileName);
        string[] argumentCopy = [.. arguments];

        ct.ThrowIfCancellationRequested();

        ToolResult result;
        try
        {
            result = await _launcher.RunAsync(absolutePath, argumentCopy, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Record(tool, argumentCopy, null, string.Empty, "Cancelled.", LogLevelKind.Error);
            throw;
        }
        catch (Win32Exception ex)
        {
            Record(tool, argumentCopy, null, string.Empty, ex.Message, LogLevelKind.Error);
            throw new PlatformException(tool.FailureCode, $"Could not start {tool.Name}.", ex);
        }

        LogLevelKind level = result.ExitCode == 0 ? LogLevelKind.Information : LogLevelKind.Warning;
        Record(tool, argumentCopy, result.ExitCode, result.StdOut, result.StdErr, level);

        return result;
    }

    private static ToolSpec Resolve(string toolFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolFileName);

        if (toolFileName.Contains('\\') || toolFileName.Contains('/') || toolFileName.Contains(':')
            || toolFileName.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("A tool name cannot contain path separators, a colon or '..'.", nameof(toolFileName));
        }

        if (!Allowlist.TryGetValue(toolFileName, out ToolSpec? spec))
        {
            throw new ArgumentException($"'{toolFileName}' is not an allowed tool.", nameof(toolFileName));
        }

        return spec;
    }

    private void Record(
        ToolSpec tool,
        string[] arguments,
        int? exitCode,
        string stdOut,
        string stdErr,
        LogLevelKind level)
    {
        var properties = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["Tool"] = tool.Name,
            ["Arguments"] = arguments,
            ["ExitCode"] = exitCode,
            ["StdOut"] = stdOut,
            ["StdErr"] = stdErr,
        };

        _log.Record(new LogEvent(EventName, properties, level));
    }

    private sealed record ToolSpec(string Name, PlatformErrorCode FailureCode);
}
