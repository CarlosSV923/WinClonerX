using WinClonerX.Application.Errors;

namespace WinClonerX.Infrastructure.Windows.Boot;

/// <summary>
/// The only way the boot configurator runs bcdedit (R7). Every call names the clone's BCD store with <c>/store</c> as its
/// first two arguments, so it cannot read or change the boot configuration of the running system.
/// <see cref="ExternalToolRunner"/> enforces the same rule for every bcdedit run.
/// </summary>
internal sealed class BcdStoreEditor
{
    private const string BcdeditFileName = "bcdedit.exe";

    private readonly ExternalToolRunner _runner;

    /// <summary>Targets the BCD store of the EFI partition mounted at <paramref name="espRoot"/>.</summary>
    internal BcdStoreEditor(ExternalToolRunner runner, string espRoot)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentException.ThrowIfNullOrWhiteSpace(espRoot);

        _runner = runner;
        StorePath = BootTargetSafety.BcdStorePathOf(espRoot, PlatformErrorCode.BootConfigFailed);
    }

    /// <summary>Store that every call uses, for example <c>S:\EFI\Microsoft\Boot\BCD</c>.</summary>
    internal string StorePath { get; }

    /// <summary>Runs <c>bcdedit /store &lt;StorePath&gt; /enum &lt;identifier&gt; /v</c>.</summary>
    internal Task<ToolResult> EnumAsync(string identifier, CancellationToken ct) =>
        RunAsync(["/enum", identifier, "/v"], ct);

    /// <summary>Runs <c>bcdedit /store &lt;StorePath&gt; /set &lt;identifier&gt; &lt;element&gt; &lt;value&gt;</c>.</summary>
    internal Task<ToolResult> SetAsync(string identifier, string element, string value, CancellationToken ct) =>
        RunAsync(["/set", identifier, element, value], ct);

    private Task<ToolResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct) =>
        _runner.RunAsync(BcdeditFileName, ["/store", StorePath, .. arguments], ct);
}
