using System.ComponentModel;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Infrastructure.Windows.Boot;

/// <summary>Captured result of one tool run.</summary>
internal sealed record ToolResult(int ExitCode, string StdOut, string StdErr);

/// <summary>Starts a process by absolute path, with an argument list, and captures its output. Tests fake it.</summary>
internal interface IToolProcessLauncher
{
    /// <summary>Runs <paramref name="absolutePath"/> with exactly <paramref name="arguments"/>. Throws <see cref="Win32Exception"/> if it cannot start.</summary>
    Task<ToolResult> RunAsync(string absolutePath, IReadOnlyList<string> arguments, CancellationToken ct);
}

/// <summary>
/// Runs the three Windows boot tools that the clone needs (bcdboot, bcdedit and reagentc), and nothing else (R7, T069).
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>Only files named exactly <c>bcdboot.exe</c>, <c>bcdedit.exe</c> or <c>reagentc.exe</c> run, and only from the
/// System32 folder, by absolute path. Names with a path separator, a colon or <c>..</c> are rejected.</item>
/// <item>Every <c>bcdedit.exe</c> call must start with <c>/store &lt;X:&gt;\EFI\Microsoft\Boot\BCD</c>, the store of a
/// clone EFI partition. Without it bcdedit would change the boot configuration of the running system, so any other
/// shape is refused before a process starts. <see cref="BcdStoreEditor"/> builds these calls.</item>
/// <item>Arguments are passed as an argument list, never as a joined string, with no shell.</item>
/// <item>Each run is recorded as an <c>ExternalToolRun</c> event (log-events.md). A non-zero exit code is logged as a
/// warning and returned to the caller, which decides whether it is a failure.</item>
/// </list>
/// </remarks>
internal sealed class ExternalToolRunner
{
    private const string EventName = "ExternalToolRun";
    private const string StoreFlag = "/store";

    private static readonly Dictionary<string, ToolSpec> Allowlist = new(StringComparer.Ordinal)
    {
        ["bcdboot.exe"] = new ToolSpec("bcdboot", PlatformErrorCode.BootConfigFailed, RequiresClonedStore: false),
        ["bcdedit.exe"] = new ToolSpec("bcdedit", PlatformErrorCode.BootConfigFailed, RequiresClonedStore: true),
        ["reagentc.exe"] = new ToolSpec("reagentc", PlatformErrorCode.RecoveryConfigFailed, RequiresClonedStore: false),
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

        if (tool.RequiresClonedStore)
        {
            AssertClonedStoreFirst(argumentCopy);
        }

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

    /// <summary>
    /// Refuses a bcdedit call unless its first two arguments are <c>/store</c> and a clone BCD store path, and no later
    /// argument is another <c>/store</c> (bcdedit could take the last one). The store may not be on the drive of the running
    /// Windows either. Fails closed: nothing starts.
    /// </summary>
    private void AssertClonedStoreFirst(string[] arguments)
    {
        bool storeFirst = arguments.Length >= 2
            && arguments[0] == StoreFlag
            && BootTargetSafety.IsClonedBcdStorePath(arguments[1]);

        bool storeRepeated = arguments.Skip(2).Any(argument => argument.StartsWith(StoreFlag, StringComparison.OrdinalIgnoreCase));

        // An unknown running drive refuses too: the check fails closed.
        string runningRoot = Path.GetPathRoot(_systemDirectory) ?? string.Empty;
        bool onRunningDrive = runningRoot.Length == 0
            || (storeFirst && char.ToUpperInvariant(arguments[1][0]) == char.ToUpperInvariant(runningRoot[0]));

        if (!storeFirst || storeRepeated || onRunningDrive)
        {
            throw new ArgumentException(
                "bcdedit may only run as '/store <X:>\\EFI\\Microsoft\\Boot\\BCD' on a drive other than the running Windows, followed by its arguments.",
                nameof(arguments));
        }
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

    private sealed record ToolSpec(string Name, PlatformErrorCode FailureCode, bool RequiresClonedStore);
}
