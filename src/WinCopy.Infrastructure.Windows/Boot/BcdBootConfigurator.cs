using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.Windows.Boot;

/// <summary>
/// Rebuilds the boot configuration of the clone (R7, T070).
/// </summary>
/// <remarks>
/// <para>The raw copy of the EFI partition already holds the BCD store of the source, with its Windows entry. If that store
/// has a default loader, it is retargeted in place with <c>bcdedit /store</c> (see <see cref="RetargetStoreAsync"/>). The
/// boot manager, the default loader, its resume object, the memory diagnostic and the WinRE loader get the clone's volumes,
/// and every entry keeps its settings (hypervisorlaunchtype, locale, recoverysequence). bcdboot is not run then. It would add
/// a second Windows entry, and the copied one would stay with devices that are unknown on the clone.</para>
/// <para>Without a store, or without a default loader in it, bcdboot creates one with
/// <c>bcdboot &lt;W&gt;:\Windows /s &lt;S&gt;: /f UEFI</c>. <c>/s</c> is mandatory. With it, bcdboot writes no firmware (NVRAM)
/// boot entries, so the boot order of the machine running WinCopy is not changed. <c>/addlast</c>, <c>/p</c> and
/// <c>/bootex</c> are never passed.</para>
/// <para>The targets are checked by <see cref="BootTargetSafety"/> before any process starts. Every bcdedit call goes through
/// <see cref="BcdStoreEditor"/>, which names the clone's store first. A bcdedit call without it would change the boot
/// configuration of the running system.</para>
/// </remarks>
internal sealed class BcdBootConfigurator : IBootConfigurator
{
    private const string BcdbootFileName = "bcdboot.exe";
    private const string BootManagerIdentifier = "{bootmgr}";
    private const string DefaultIdentifier = "{default}";
    private const string MemDiagIdentifier = "{memdiag}";

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
    public async Task RebuildAsync(string windowsRoot, string espRoot, string? recoveryRoot, CancellationToken ct)
    {
        _safety.AssertCloneRoots(windowsRoot, espRoot, PlatformErrorCode.BootConfigFailed);

        char windowsLetter = BootTargetSafety.DriveLetterOf(windowsRoot, PlatformErrorCode.BootConfigFailed);
        char espLetter = BootTargetSafety.DriveLetterOf(espRoot, PlatformErrorCode.BootConfigFailed);

        char? recoveryLetter = null;
        if (recoveryRoot is not null)
        {
            _safety.AssertCloneRoots(windowsRoot, recoveryRoot, PlatformErrorCode.BootConfigFailed);
            recoveryLetter = BootTargetSafety.DriveLetterOf(recoveryRoot, PlatformErrorCode.BootConfigFailed);

            if (recoveryLetter == espLetter)
            {
                throw new PlatformException(
                    PlatformErrorCode.BootConfigFailed,
                    $"The recovery root and the EFI root both use the drive letter '{espLetter}:'.");
            }
        }

        var store = new BcdStoreEditor(_runner, espRoot);
        if (_environment.FileExists(store.StorePath)
            && await RetargetStoreAsync(store, windowsLetter, espLetter, recoveryLetter, ct).ConfigureAwait(false))
        {
            return;
        }

        IReadOnlyList<string> arguments = [$"{windowsLetter}:\\Windows", "/s", $"{espLetter}:", "/f", "UEFI"];

        ToolResult result = await _runner.RunAsync(BcdbootFileName, arguments, ct).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            throw new PlatformException(
                PlatformErrorCode.BootConfigFailed,
                $"bcdboot exited with code {result.ExitCode}. {CombinedOutput(result)}");
        }

        if (!_environment.FileExists(store.StorePath))
        {
            throw new PlatformException(
                PlatformErrorCode.BootConfigFailed,
                $"bcdboot finished but no BCD store was created at '{store.StorePath}'.");
        }
    }

    /// <summary>
    /// Retargets the cloned store in place. Returns <c>false</c> when the store has no default loader, so the caller runs
    /// bcdboot. Otherwise it returns <c>true</c> only after the default loader shows the clone's Windows volume.
    /// </summary>
    /// <remarks>
    /// Every answer is read and checked before the first write. A malformed answer throws
    /// <see cref="PlatformErrorCode.BootConfigFailed"/> with the reason, and nothing has been written. Writes are one
    /// <c>/set</c> each, and a failed one throws with its output. The last step is <c>/enum {default} /v</c>, which must
    /// show <c>partition=W:</c> for device and osdevice.
    /// </remarks>
    private static async Task<bool> RetargetStoreAsync(
        BcdStoreEditor store, char windowsLetter, char espLetter, char? recoveryLetter, CancellationToken ct)
    {
        try
        {
            IReadOnlyList<BcdSet>? sets = await PlanRetargetAsync(store, windowsLetter, espLetter, recoveryLetter, ct)
                .ConfigureAwait(false);
            if (sets is null)
            {
                return false;
            }

            foreach (BcdSet set in sets)
            {
                ToolResult written = await store.SetAsync(set.Identifier, set.Element, set.Value, ct).ConfigureAwait(false);
                EnsureSucceeded(written, $"set '{set.Element}' of {set.Identifier}");
            }

            await VerifyDefaultLoaderAsync(store, windowsLetter, ct).ConfigureAwait(false);
            return true;
        }
        catch (FormatException ex)
        {
            throw new PlatformException(
                PlatformErrorCode.BootConfigFailed,
                $"bcdedit printed output that could not be read: {ex.Message}.",
                ex);
        }
    }

    /// <summary>
    /// Reads the store and lists the writes that retarget it, in order. Returns <c>null</c> when there is no default loader.
    /// Reads: <c>{bootmgr}</c>, <c>{default}</c>, then the resume object, <c>{memdiag}</c> and the WinRE loader when the store
    /// has them and the recovery letter is known. Every absent optional entry is skipped, not an error.
    /// </summary>
    private static async Task<IReadOnlyList<BcdSet>?> PlanRetargetAsync(
        BcdStoreEditor store, char windowsLetter, char espLetter, char? recoveryLetter, CancellationToken ct)
    {
        BcdEntry bootManager = await ReadEntryAsync(store, BootManagerIdentifier, "read the boot manager", ct).ConfigureAwait(false);

        string? defaultValue = bootManager.ElementOrNull("default");
        if (defaultValue is null)
        {
            return null;
        }

        BcdEntry loader = await ReadEntryAsync(store, DefaultIdentifier, "read the default loader", ct).ConfigureAwait(false);
        string loaderId = RequireGuid(loader.Identifier);
        if (!string.Equals(loaderId, RequireGuid(defaultValue), StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("the default loader is not the one that the boot manager names");
        }

        var sets = new List<BcdSet>
        {
            new(BootManagerIdentifier, "device", PartitionOf(espLetter)),
            new(loaderId, "device", PartitionOf(windowsLetter)),
            new(loaderId, "osdevice", PartitionOf(windowsLetter)),
        };

        string? resumeId = GuidElementOrNull(loader, "resumeobject");
        if (resumeId is not null)
        {
            BcdEntry resume = await ReadEntryAsync(store, resumeId, "read the resume object", ct).ConfigureAwait(false);
            sets.Add(new BcdSet(resumeId, "device", PartitionOf(windowsLetter)));

            if (resume.ElementOrNull("filedevice") is not null)
            {
                sets.Add(new BcdSet(resumeId, "filedevice", PartitionOf(windowsLetter)));
            }
        }

        // {memdiag} is optional. bcdedit fails to enumerate an entry that the store does not have, so a failure means absent.
        ToolResult memDiag = await store.EnumAsync(MemDiagIdentifier, ct).ConfigureAwait(false);
        if (memDiag.ExitCode == 0)
        {
            _ = BcdEnumParser.Parse(memDiag.StdOut);
            sets.Add(new BcdSet(MemDiagIdentifier, "device", PartitionOf(espLetter)));
        }

        string? recoveryId = GuidElementOrNull(loader, "recoverysequence");
        if (recoveryId is not null && recoveryLetter is char letter)
        {
            BcdEntry recovery = await ReadEntryAsync(store, recoveryId, "read the recovery loader", ct).ConfigureAwait(false);
            string device = recovery.ElementOrNull("device") ?? throw new FormatException("the recovery loader has no device");
            RamdiskDevice ramdisk = BcdEnumParser.ParseRamdiskDevice(device);

            string ramdiskValue = $"ramdisk=[{letter}:]{ramdisk.WimPath},{ramdisk.OptionsIdentifier}";
            sets.Add(new BcdSet(recoveryId, "device", ramdiskValue));
            sets.Add(new BcdSet(recoveryId, "osdevice", ramdiskValue));
            sets.Add(new BcdSet(ramdisk.OptionsIdentifier, "ramdisksdidevice", PartitionOf(letter)));
        }

        return sets;
    }

    private static async Task VerifyDefaultLoaderAsync(BcdStoreEditor store, char windowsLetter, CancellationToken ct)
    {
        ToolResult result = await store.EnumAsync(DefaultIdentifier, ct).ConfigureAwait(false);
        EnsureSucceeded(result, "verify the default loader");

        BcdEntry loader = BcdEnumParser.Parse(result.StdOut);
        string expected = PartitionOf(windowsLetter);
        bool pointsAtClone =
            string.Equals(loader.ElementOrNull("device"), expected, StringComparison.OrdinalIgnoreCase)
            && string.Equals(loader.ElementOrNull("osdevice"), expected, StringComparison.OrdinalIgnoreCase);

        if (!pointsAtClone)
        {
            throw new PlatformException(
                PlatformErrorCode.BootConfigFailed,
                $"After the retarget the default loader does not point to '{windowsLetter}:'. {CombinedOutput(result)}");
        }
    }

    private static async Task<BcdEntry> ReadEntryAsync(
        BcdStoreEditor store, string identifier, string action, CancellationToken ct)
    {
        ToolResult result = await store.EnumAsync(identifier, ct).ConfigureAwait(false);
        EnsureSucceeded(result, action);

        return BcdEnumParser.Parse(result.StdOut);
    }

    private static string? GuidElementOrNull(BcdEntry entry, string element)
    {
        string? value = entry.ElementOrNull(element);

        return value is null ? null : RequireGuid(value);
    }

    private static string RequireGuid(string value) =>
        BcdEnumParser.IsGuid(value) ? value : throw new FormatException($"'{value}' is not a GUID identifier");

    private static string PartitionOf(char driveLetter) => $"partition={driveLetter}:";

    private static void EnsureSucceeded(ToolResult result, string action)
    {
        if (result.ExitCode != 0)
        {
            throw new PlatformException(
                PlatformErrorCode.BootConfigFailed,
                $"bcdedit could not {action} (exit code {result.ExitCode}). {CombinedOutput(result)}");
        }
    }

    private static string CombinedOutput(ToolResult result) =>
        string.Join(
            Environment.NewLine,
            new[] { result.StdOut, result.StdErr }.Select(text => text.Trim()).Where(text => text.Length > 0));

    /// <summary>One <c>bcdedit /set</c> to run against the store.</summary>
    private readonly record struct BcdSet(string Identifier, string Element, string Value);
}
