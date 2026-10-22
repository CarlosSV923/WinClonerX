using WinClonerX.Infrastructure.Windows.Boot;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Tests.Fakes;

namespace WinClonerX.Infrastructure.Windows.Tests.Boot;

// Records every process start and returns a canned result or throws a canned failure. Nothing runs.
// When Respond is set, it answers each run instead, so a test can give bcdedit its own output.
internal sealed class RecordingToolLauncher : IToolProcessLauncher
{
    public List<(string Path, IReadOnlyList<string> Arguments)> Calls { get; } = [];

    public ToolResult Result { get; set; } = new(0, string.Empty, string.Empty);

    public Exception? Failure { get; set; }

    public Func<string, IReadOnlyList<string>, ToolResult>? Respond { get; set; }

    public Task<ToolResult> RunAsync(string absolutePath, IReadOnlyList<string> arguments, CancellationToken ct)
    {
        Calls.Add((absolutePath, arguments));

        if (Failure is not null)
        {
            return Task.FromException<ToolResult>(Failure);
        }

        return Task.FromResult(Respond is null ? Result : Respond(absolutePath, arguments));
    }
}

// In-memory answers for the guard's OS queries. A volume that was not set up throws, as the real call would.
internal sealed class FakeBootTargetEnvironment : IBootTargetEnvironment
{
    public Dictionary<string, string> VolumeGuids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> VolumeLookups { get; } = [];

    public string RunningDrive { get; set; } = @"C:\";

    public HashSet<string> Files { get; } = new(StringComparer.OrdinalIgnoreCase);

    public string VolumeGuidPathOf(string driveRoot)
    {
        VolumeLookups.Add(driveRoot);

        return VolumeGuids.TryGetValue(driveRoot, out string? guid)
            ? guid
            : throw new System.ComponentModel.Win32Exception(2, $"No volume is mounted at {driveRoot}.");
    }

    public string RunningWindowsDriveRoot() => RunningDrive;

    public bool FileExists(string path) => Files.Contains(path);
}

// In-memory extents. Keys are device paths without a trailing backslash, the form the real reader opens.
internal sealed class FakeVolumeExtents : IVolumeExtentsReader
{
    public Dictionary<string, IReadOnlySet<uint>> DisksByVolume { get; } = new(StringComparer.Ordinal);

    // Set this to make every extents query fail.
    public Exception? Failure { get; set; }

    public IReadOnlySet<uint> DiskNumbersOf(string volumePath)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        return DisksByVolume.TryGetValue(volumePath, out IReadOnlySet<uint>? disks)
            ? disks
            : throw new InvalidOperationException($"No extents faked for '{volumePath}'.");
    }

    public string WindowsVolumePath() => BootScenario.RunningWindowsDevice;

    public string SystemPartitionVolumePath() => BootScenario.RunningBootPartitionDevice;

    public STORAGE_BUS_TYPE BusTypeOf(int diskNumber) => throw new NotSupportedException();
}

// A running system on disk 0 and a clone on disk 1, with the Windows, EFI and recovery volumes mounted at W:, S: and R:.
internal sealed class BootScenario
{
    public const string SystemDirectory = @"C:\FakeWindows\System32";

    public const string WindowsRoot = @"W:\";
    public const string EspRoot = @"S:\";
    public const string RecoveryRoot = @"R:\";

    public const string RunningWindowsDevice = @"\\.\C:";
    public const string RunningBootPartitionDevice = @"\\?\GLOBALROOT\Device\HarddiskVolume1";
    public const string WindowsVolumeDevice = @"\\?\Volume{11111111-1111-1111-1111-111111111111}";
    public const string EspVolumeDevice = @"\\?\Volume{22222222-2222-2222-2222-222222222222}";
    public const string RecoveryVolumeDevice = @"\\?\Volume{33333333-3333-3333-3333-333333333333}";

    public BootScenario()
    {
        Launcher = new RecordingToolLauncher();
        Log = new RecordingExecutionLog();
        Runner = new ExternalToolRunner(Launcher, Log, SystemDirectory);
        Safety = new BootTargetSafety(Os, Extents);

        Os.VolumeGuids[WindowsRoot] = WindowsVolumeDevice + @"\";
        Os.VolumeGuids[EspRoot] = EspVolumeDevice + @"\";
        Os.VolumeGuids[RecoveryRoot] = RecoveryVolumeDevice + @"\";

        Os.Files.Add(WindowsRoot + @"Windows\System32\config\SYSTEM");

        Extents.DisksByVolume[RunningWindowsDevice] = new HashSet<uint> { 0 };
        Extents.DisksByVolume[RunningBootPartitionDevice] = new HashSet<uint> { 0 };
        Extents.DisksByVolume[WindowsVolumeDevice] = new HashSet<uint> { 1 };
        Extents.DisksByVolume[EspVolumeDevice] = new HashSet<uint> { 1 };
        Extents.DisksByVolume[RecoveryVolumeDevice] = new HashSet<uint> { 1 };
    }

    public FakeBootTargetEnvironment Os { get; } = new();

    public FakeVolumeExtents Extents { get; } = new();

    public RecordingToolLauncher Launcher { get; }

    public RecordingExecutionLog Log { get; }

    public ExternalToolRunner Runner { get; }

    public BootTargetSafety Safety { get; }

    public static string BcdFile => EspRoot + @"EFI\Microsoft\Boot\BCD";

    public static string WinreImage => RecoveryRoot + @"Recovery\WindowsRE\Winre.wim";
}
