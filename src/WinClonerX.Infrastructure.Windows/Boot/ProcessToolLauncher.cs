using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WinClonerX.Infrastructure.Windows.Boot;

/// <summary>
/// Win32 implementation of <see cref="IToolProcessLauncher"/>. No shell: the file is started directly, with
/// <c>ProcessStartInfo.ArgumentList</c> and no window. Output is read while the process runs, so a full pipe cannot
/// block it.
/// </summary>
internal sealed class ProcessToolLauncher : IToolProcessLauncher
{
    /// <summary>
    /// Encoding of the tools' output: the OEM code page of the culture, which is the console code page that bcdedit, bcdboot
    /// and reagentc write with. Read once, on first use.
    /// </summary>
    private static readonly Encoding ToolOutputEncoding = ResolveOutputEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

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
            StandardOutputEncoding = ToolOutputEncoding,
            StandardErrorEncoding = ToolOutputEncoding,
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

    /// <summary>
    /// Returns the encoding for the given OEM code page. The code pages beyond ASCII and UTF-16 are registered with
    /// <see cref="CodePagesEncodingProvider"/> first. If the code page is not available, returns the default encoding.
    /// </summary>
    internal static Encoding ResolveOutputEncoding(int oemCodePage)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(oemCodePage);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            // Without the code page the output is read with the default encoding, as before this fix.
            return Encoding.Default;
        }
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
