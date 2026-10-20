using WinCopy.Infrastructure.Windows.Boot;

namespace WinCopy.Infrastructure.Windows.Tests.Boot;

// In-memory BCD store that answers bcdedit the way the real tool does for the calls the configurator makes. /enum prints
// one entry: a localized title, the identifier as a GUID under a localized key, and the elements with their English names.
// /set changes one element. An identifier the store does not have fails with exit code 1. Nothing runs.
// The store entries model a Windows 11 install copied raw: every device points at the source disk.
internal sealed class FakeBcdStore
{
    public const string BootManager = "{9dea862c-5cdd-4e70-acc1-f32b344d4795}";
    public const string MemDiag = "{b2721d73-1db4-4c62-bf78-c548a880142d}";
    public const string Loader = "{4c2e0d7a-3f1b-4a8e-9b6d-2f7a1c3e5b01}";
    public const string Resume = "{6d8f1e22-7a4c-4b9e-8e3d-1a2b3c4d5e6f}";
    public const string Recovery = "{a1b2c3d4-e5f6-4a7b-8c9d-0e1f2a3b4c5d}";
    public const string RamdiskOptions = "{f0e1d2c3-b4a5-4968-8776-655443322110}";

    private const string SourceWindowsDevice = @"partition=\Device\HarddiskVolume2";
    private const string WinreDevice = @"ramdisk=[\Device\HarddiskVolume4]\Recovery\WindowsRE\Winre.wim," + RamdiskOptions;

    public FakeBcdStore()
    {
        Entries[BootManager] = new FakeEntry(
            "Administrador de arranque de Windows",
            new Dictionary<string, string>
            {
                ["device"] = @"partition=\Device\HarddiskVolume1",
                ["description"] = "Windows Boot Manager",
                ["locale"] = "es-MX",
                ["default"] = Loader,
                ["displayorder"] = Loader,
                ["toolsdisplayorder"] = MemDiag,
                ["timeout"] = "30",
            });

        Entries[Loader] = new FakeEntry(
            "Cargador de arranque de Windows",
            new Dictionary<string, string>
            {
                ["device"] = SourceWindowsDevice,
                ["path"] = @"\Windows\system32\winload.efi",
                ["description"] = "Windows 11",
                ["locale"] = "es-MX",
                ["inherit"] = "{bootloadersettings}",
                ["recoverysequence"] = Recovery,
                ["recoveryenabled"] = "Yes",
                ["osdevice"] = SourceWindowsDevice,
                ["systemroot"] = @"\Windows",
                ["resumeobject"] = Resume,
                ["nx"] = "OptIn",
                ["bootmenupolicy"] = "Standard",
                ["hypervisorlaunchtype"] = "Auto",
            });

        Entries[Resume] = new FakeEntry(
            "Cargador de reanudación de Windows",
            new Dictionary<string, string>
            {
                ["device"] = SourceWindowsDevice,
                ["path"] = @"\Windows\system32\winresume.efi",
                ["description"] = "Windows Resume Application",
                ["filedevice"] = SourceWindowsDevice,
                ["filepath"] = @"\hiberfil.sys",
            });

        Entries[MemDiag] = new FakeEntry(
            "Herramienta de diagnóstico de memoria de Windows",
            new Dictionary<string, string>
            {
                ["device"] = SourceWindowsDevice,
                ["path"] = @"\EFI\Microsoft\Boot\memtest.efi",
                ["description"] = "Windows Memory Diagnostic",
            });

        Entries[Recovery] = new FakeEntry(
            "Windows Recovery Environment",
            new Dictionary<string, string>
            {
                ["device"] = WinreDevice,
                ["path"] = @"\windows\system32\winload.efi",
                ["description"] = "Windows Recovery Environment",
                ["osdevice"] = WinreDevice,
                ["systemroot"] = @"\windows",
            });

        Entries[RamdiskOptions] = new FakeEntry(
            "Opciones de dispositivo de RAM",
            new Dictionary<string, string>
            {
                ["ramdisksdidevice"] = @"partition=\Device\HarddiskVolume4",
                ["ramdisksdipath"] = @"\Recovery\WindowsRE\boot.sdi",
            });
    }

    /// <summary>Entries of the store by GUID. A test can remove one to model a store without it.</summary>
    public Dictionary<string, FakeEntry> Entries { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Writes "identifier element" that are accepted but not stored. Models a store that ignores a write.</summary>
    public HashSet<string> IgnoredWrites { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Exit code of every <c>/set</c>. Zero writes normally.</summary>
    public int WriteExitCode { get; set; }

    /// <summary>Answers one bcdedit run. The arguments must start with <c>/store</c> and the clone store, or the test fails.</summary>
    public ToolResult Run(IReadOnlyList<string> arguments)
    {
        if (arguments.Count < 4 || arguments[0] != "/store" || arguments[1] != BootScenario.BcdFile)
        {
            throw new InvalidOperationException("bcdedit was started without the clone store as its first two arguments.");
        }

        string verb = arguments[2];
        string? guid = ResolveIdentifier(arguments[3]);
        if (guid is null || !Entries.ContainsKey(guid))
        {
            return new ToolResult(1, string.Empty, "El elemento no se ha encontrado.");
        }

        if (verb == "/enum" && arguments.Count == 5 && arguments[4] == "/v")
        {
            return new ToolResult(0, Render(guid), string.Empty);
        }

        if (verb == "/set" && arguments.Count == 6)
        {
            if (WriteExitCode != 0)
            {
                return new ToolResult(WriteExitCode, string.Empty, "Acceso denegado.");
            }

            if (!IgnoredWrites.Contains($"{guid} {arguments[4]}"))
            {
                Entries[guid].Elements[arguments[4]] = arguments[5];
            }

            return new ToolResult(0, "La operación se completó correctamente.", string.Empty);
        }

        return new ToolResult(1, string.Empty, "Argumentos no válidos.");
    }

    private string? ResolveIdentifier(string identifier) => identifier switch
    {
        "{bootmgr}" => BootManager,
        "{memdiag}" => MemDiag,
        "{default}" => Entries.TryGetValue(BootManager, out FakeEntry? bootManager)
            && bootManager.Elements.TryGetValue("default", out string? defaultLoader)
                ? defaultLoader
                : null,
        _ => identifier,
    };

    private string Render(string guid)
    {
        FakeEntry entry = Entries[guid];
        var lines = new List<string>
        {
            entry.Title,
            new string('-', entry.Title.Length),
            $"{"identificador",-24}{guid}",
        };

        lines.AddRange(entry.Elements.Select(pair => $"{pair.Key,-24}{pair.Value}"));

        return string.Join("\r\n", lines) + "\r\n\r\n";
    }

    /// <summary>One entry of the fake store: its localized title and its elements.</summary>
    public sealed record FakeEntry(string Title, Dictionary<string, string> Elements);
}
