using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>
/// Rebuilds the boot files of the clone with <c>bcdboot &lt;W&gt;:\Windows /s &lt;S&gt;: /f UEFI</c> (R7, T070).
/// </summary>
/// <remarks>
/// <para><c>/s</c> is mandatory. With it, bcdboot writes no firmware (NVRAM) boot entries, so the boot order of the
/// machine running WinCopy is not changed. <c>/addlast</c>, <c>/p</c> and <c>/bootex</c> are never passed.</para>
/// <para>The targets are checked by <see cref="BootTargetSafety"/> before any process starts.</para>
/// </remarks>
internal sealed class BcdBootConfigurator : IBootConfigurator
{
    private const string BcdbootFileName = "bcdboot.exe";
    private const string ClonedBcdRelativePath = @"EFI\Microsoft\Boot\BCD";

    private readonly ExternalToolRunner _runner;
    private readonly BootTargetSafety _safety;
    private readonly IBootTargetEnvironment _environment;

    internal BcdBootConfigurator(ExternalToolRunner runner, BootTargetSafety safety, IBootTargetEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(safety);
        ArgumentNullException.ThrowIfNull(environment);

        _runner = runner;
        _safety = safety;
        _environment = environment;
    }

    /// <summary>Production instance: real tools, real volume queries, real file checks.</summary>
    internal static BcdBootConfigurator ForThisMachine(IExecutionLog log)
    {
        var environment = new Win32BootTargetEnvironment();
        var safety = new BootTargetSafety(environment, new WindowsVolumeExtentsReader());

        return new BcdBootConfigurator(ExternalToolRunner.ForThisMachine(log), safety, environment);
    }

    /// <inheritdoc />
    public async Task RebuildAsync(string windowsRoot, string espRoot, CancellationToken ct)
    {
        _safety.AssertCloneRoots(windowsRoot, espRoot, PlatformErrorCode.BootConfigFailed);

        char windowsLetter = BootTargetSafety.DriveLetterOf(windowsRoot, PlatformErrorCode.BootConfigFailed);
        char espLetter = BootTargetSafety.DriveLetterOf(espRoot, PlatformErrorCode.BootConfigFailed);

        IReadOnlyList<string> arguments = [$"{windowsLetter}:\\Windows", "/s", $"{espLetter}:", "/f", "UEFI"];

        ToolResult result = await _runner.RunAsync(BcdbootFileName, arguments, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new PlatformException(
                PlatformErrorCode.BootConfigFailed,
                $"bcdboot exited with code {result.ExitCode}. {CombinedOutput(result)}");
        }

        if (!_environment.FileExists(espRoot + ClonedBcdRelativePath))
        {
            throw new PlatformException(
                PlatformErrorCode.BootConfigFailed,
                $"bcdboot finished but no BCD store was created at '{espRoot}{ClonedBcdRelativePath}'.");
        }
    }

    private static string CombinedOutput(ToolResult result) =>
        string.Join(
            Environment.NewLine,
            new[] { result.StdOut, result.StdErr }.Select(text => text.Trim()).Where(text => text.Length > 0));
}
