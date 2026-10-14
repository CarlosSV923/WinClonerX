using System.ComponentModel;
using System.Diagnostics;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>
/// Win32 implementation of <see cref="IToolProcessLauncher"/>. No shell: the file is started directly, with
/// <c>ProcessStartInfo.ArgumentList</c> and no window. Output is read while the process runs, so a full pipe cannot
/// block it.
/// </summary>
internal sealed class ProcessToolLauncher : IToolProcessLauncher
{
    public async Task<ToolResult> RunAsync(string absolutePath, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(absolutePath);
        ArgumentNullException.ThrowIfNull(arguments);

        var startInfo = new ProcessStartInfo(absolutePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new Win32Exception($"Process '{absolutePath}' did not start.");
        }

        Task<string> stdOut = process.StandardOutput.ReadToEndAsync(ct);
        Task<string> stdErr = process.StandardError.ReadToEndAsync(ct);

        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            KillQuietly(process);
            throw;
        }

        return new ToolResult(process.ExitCode, await stdOut.ConfigureAwait(false), await stdErr.ConfigureAwait(false));
    }

    private static void KillQuietly(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            // The process already exited between the cancellation and the kill. Nothing is left to stop.
        }
    }
}
