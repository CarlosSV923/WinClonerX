using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Infrastructure.Windows.RawIo;

namespace WinClonerX.Infrastructure.Windows.Boot;

/// <summary>
/// Points WinRE of the clone at its recovery partition with
/// <c>reagentc /setreimage /path &lt;R&gt;:\Recovery\WindowsRE /target &lt;W&gt;:\Windows</c> (R7, T070).
/// </summary>
/// <remarks>
/// WinRE is a warning, not a blocker: a missing image or a failed reagentc run returns
/// <see cref="RecoveryConfigResult"/> with <c>Configured = false</c>. Unsafe or malformed roots still throw
/// <see cref="PlatformException"/> (<see cref="PlatformErrorCode.RecoveryConfigFailed"/>), because they mean the
/// request itself is wrong.
/// </remarks>
internal sealed class ReagentcRecoveryConfigurator : IRecoveryConfigurator
{
    private const string ReagentcFileName = "reagentc.exe";
    private const string WinreImageRelativePath = @"Recovery\WindowsRE\Winre.wim";

    private readonly ExternalToolRunner _runner;
    private readonly BootTargetSafety _safety;
    private readonly IBootTargetEnvironment _environment;

    internal ReagentcRecoveryConfigurator(ExternalToolRunner runner, BootTargetSafety safety, IBootTargetEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);
        ArgumentNullException.ThrowIfNull(environment);

        _runner = runner;
        _safety = safety;
        _environment = environment;
    }

    /// <summary>Production instance: real tools, real volume queries, real file checks.</summary>
    internal static ReagentcRecoveryConfigurator ForThisMachine(IExecutionLog log)
    {
        var environment = new Win32BootTargetEnvironment();
        var safety = new BootTargetSafety(environment, new WindowsVolumeExtentsReader());

        return new ReagentcRecoveryConfigurator(ExternalToolRunner.ForThisMachine(log), safety, environment);
    }

    /// <inheritdoc />
    public async Task<RecoveryConfigResult> ConfigureAsync(string windowsRoot, string recoveryRoot, CancellationToken ct)
    {
        _safety.AssertCloneRoots(windowsRoot, recoveryRoot, PlatformErrorCode.RecoveryConfigFailed);

        char windowsLetter = BootTargetSafety.DriveLetterOf(windowsRoot, PlatformErrorCode.RecoveryConfigFailed);
        char recoveryLetter = BootTargetSafety.DriveLetterOf(recoveryRoot, PlatformErrorCode.RecoveryConfigFailed);

        if (!_environment.FileExists(recoveryRoot + WinreImageRelativePath))
        {
            return new RecoveryConfigResult(false, "WinRE image not found on the recovery partition");
        }

        IReadOnlyList<string> arguments =
            ["/setreimage", "/path", $"{recoveryLetter}:\\Recovery\\WindowsRE", "/target", $"{windowsLetter}:\\Windows"];

        ToolResult result;
        try
        {
            result = await _runner.RunAsync(ReagentcFileName, arguments, ct).ConfigureAwait(false);
        }
        catch (PlatformException ex)
        {
            // reagentc could not start. That is a WinRE problem, so it is reported as a warning, not thrown.
            return new RecoveryConfigResult(false, ex.TechnicalDetail);
        }

        if (result.ExitCode == 0)
        {
            return new RecoveryConfigResult(true, null);
        }

        return new RecoveryConfigResult(false, CombinedOutput(result));
    }

    private static string CombinedOutput(ToolResult result) =>
        string.Join(
            Environment.NewLine,
            new[] { result.StdOut, result.StdErr }.Select(text => text.Trim()).Where(text => text.Length > 0));
}
