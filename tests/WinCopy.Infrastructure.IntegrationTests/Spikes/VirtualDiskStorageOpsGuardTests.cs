using Shouldly;
using WinCopy.Infrastructure.IntegrationTests.Fixtures;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinCopy.Infrastructure.IntegrationTests.Spikes;

// Pure tests of the safety gate in VirtualDiskStorageOps. No disk is touched and no process is started: the runner and the
// volume reader are fakes, and the runner's Scripts list proves whether a command would have reached PowerShell.
[Trait("Category", "Integration")]
[Trait("Spike", "S3")]
public sealed class VirtualDiskStorageOpsGuardTests
{
    // Numbers that no fixture attaches in this process. They are registered only for the duration of a test.
    private const int UnregisteredDiskNumber = 4_000_001;
    private const int NotVirtualDiskNumber = 4_000_002;
    private const int WindowsDiskNumber = 4_000_003;
    private const int SystemDiskNumber = 4_000_004;
    private const int OtherVolumeDiskNumber = 4_000_005;
    private const int AcceptedDiskNumber = 4_000_006;

    private const string OtherVolumePath = @"\\?\Volume{00000000-0000-0000-0000-000000000001}";

    [Fact]
    public void RefusesUnregisteredDiskBeforeAnyProcessStarts()
    {
        var runner = new RecordingRunner();
        var reader = new FakeVolumeReader();
        var ops = new VirtualDiskStorageOps(runner, reader);

        Should.Throw<UnsafeStorageTargetException>(() => ops.InitializeGpt(UnregisteredDiskNumber));
        Should.Throw<UnsafeStorageTargetException>(() => ops.CreateMaximumPartitionWithLetter(UnregisteredDiskNumber));
        Should.Throw<UnsafeStorageTargetException>(() => ops.CreatePartitionWithoutLetter(UnregisteredDiskNumber, 1024 * 1024));
        Should.Throw<UnsafeStorageTargetException>(() => ops.AssignDriveLetter(UnregisteredDiskNumber, 1));
        Should.Throw<UnsafeStorageTargetException>(() => ops.GetPartition(UnregisteredDiskNumber, 1));
        Should.Throw<UnsafeStorageTargetException>(() => ops.RemoveDriveLetter(UnregisteredDiskNumber, 1, 'Z'));
        Should.Throw<UnsafeStorageTargetException>(() => ops.FormatNtfs(UnregisteredDiskNumber, 'Z'));
        Should.Throw<UnsafeStorageTargetException>(() => ops.EnableBitLockerWithPassword(UnregisteredDiskNumber, 'Z', "Wc-test"));
        Should.Throw<UnsafeStorageTargetException>(() => ops.GetBitLockerStatus(UnregisteredDiskNumber, 'Z'));
        Should.Throw<UnsafeStorageTargetException>(() => ops.AssertLetterOnDisk('Z', UnregisteredDiskNumber));

        runner.Scripts.ShouldBeEmpty();
        reader.Calls.ShouldBe(0);
    }

    [Fact]
    public void RefusesNewCommandsOnUnregisteredDiskBeforeAnyProcessStarts()
    {
        var runner = new RecordingRunner();
        var reader = new FakeVolumeReader();
        var ops = new VirtualDiskStorageOps(runner, reader);

        Should.Throw<UnsafeStorageTargetException>(
            () => ops.CreateGptPartition(UnregisteredDiskNumber, 1024 * 1024, Guid.NewGuid(), assignLetter: true));
        Should.Throw<UnsafeStorageTargetException>(() => ops.FormatFat32(UnregisteredDiskNumber, 'Z', "SYSTEM"));
        Should.Throw<UnsafeStorageTargetException>(() => ops.FormatNtfs(UnregisteredDiskNumber, 'Z', "Windows"));

        runner.Scripts.ShouldBeEmpty();
        reader.Calls.ShouldBe(0);
    }

    [Fact]
    public void RefusesVolumeLabelThatCouldEndThePowerShellQuoting()
    {
        VirtualDiskRegistry.Register(AcceptedDiskNumber);
        try
        {
            var runner = new RecordingRunner();
            var reader = new FakeVolumeReader();
            reader.DisksByVolume[FakeVolumeReader.WindowsVolume] = [0u];
            reader.DisksByVolume[FakeVolumeReader.SystemVolume] = [0u];
            var ops = new VirtualDiskStorageOps(runner, reader);

            Should.Throw<ArgumentException>(() => ops.FormatNtfs(AcceptedDiskNumber, 'Z', "Win'dows"));
            Should.Throw<ArgumentException>(() => ops.FormatFat32(AcceptedDiskNumber, 'Z', "TOO-LONG-LABEL"));
            Should.Throw<ArgumentException>(() => ops.FormatNtfs(AcceptedDiskNumber, 'Z', new string('N', 33)));

            runner.Scripts.ShouldBeEmpty();
        }
        finally
        {
            VirtualDiskRegistry.Unregister(AcceptedDiskNumber);
        }
    }

    [Fact]
    public void RefusesRegisteredDiskThatIsNotFileBackedVirtual()
    {
        VirtualDiskRegistry.Register(NotVirtualDiskNumber);
        try
        {
            var runner = new RecordingRunner();
            var reader = new FakeVolumeReader { BusType = STORAGE_BUS_TYPE.BusTypeNvme };
            var ops = new VirtualDiskStorageOps(runner, reader);

            Should.Throw<UnsafeStorageTargetException>(() => ops.InitializeGpt(NotVirtualDiskNumber));

            runner.Scripts.ShouldBeEmpty();
        }
        finally
        {
            VirtualDiskRegistry.Unregister(NotVirtualDiskNumber);
        }
    }

    [Fact]
    public void RefusesRegisteredDiskThatHostsRunningWindows()
    {
        VirtualDiskRegistry.Register(WindowsDiskNumber);
        try
        {
            var runner = new RecordingRunner();
            var reader = new FakeVolumeReader();
            reader.DisksByVolume[FakeVolumeReader.WindowsVolume] = [(uint)WindowsDiskNumber];
            reader.DisksByVolume[FakeVolumeReader.SystemVolume] = [0u];
            var ops = new VirtualDiskStorageOps(runner, reader);

            Should.Throw<UnsafeStorageTargetException>(() => ops.InitializeGpt(WindowsDiskNumber));

            runner.Scripts.ShouldBeEmpty();
        }
        finally
        {
            VirtualDiskRegistry.Unregister(WindowsDiskNumber);
        }
    }

    [Fact]
    public void RefusesRegisteredDiskThatHostsTheSystemPartition()
    {
        VirtualDiskRegistry.Register(SystemDiskNumber);
        try
        {
            var runner = new RecordingRunner();
            var reader = new FakeVolumeReader();
            reader.DisksByVolume[FakeVolumeReader.WindowsVolume] = [0u];
            reader.DisksByVolume[FakeVolumeReader.SystemVolume] = [(uint)SystemDiskNumber];
            var ops = new VirtualDiskStorageOps(runner, reader);

            Should.Throw<UnsafeStorageTargetException>(() => ops.FormatNtfs(SystemDiskNumber, 'Z'));

            runner.Scripts.ShouldBeEmpty();
        }
        finally
        {
            VirtualDiskRegistry.Unregister(SystemDiskNumber);
        }
    }

    [Fact]
    public void RefusesLockingVolumeThatIsNotOnTheTargetDisk()
    {
        VirtualDiskRegistry.Register(OtherVolumeDiskNumber);
        try
        {
            var runner = new RecordingRunner();
            var reader = new FakeVolumeReader();
            reader.DisksByVolume[FakeVolumeReader.WindowsVolume] = [0u];
            reader.DisksByVolume[FakeVolumeReader.SystemVolume] = [0u];
            reader.DisksByVolume[OtherVolumePath] = [0u];
            var ops = new VirtualDiskStorageOps(runner, reader);

            Should.Throw<UnsafeStorageTargetException>(() => ops.AssertVolumeOnDisk(OtherVolumePath, OtherVolumeDiskNumber));

            runner.Scripts.ShouldBeEmpty();
        }
        finally
        {
            VirtualDiskRegistry.Unregister(OtherVolumeDiskNumber);
        }
    }

    [Fact]
    public void RunsTheCommandOnlyForRegisteredFileBackedDiskOffWindowsAndSystem()
    {
        VirtualDiskRegistry.Register(AcceptedDiskNumber);
        try
        {
            var runner = new RecordingRunner();
            var reader = new FakeVolumeReader();
            reader.DisksByVolume[FakeVolumeReader.WindowsVolume] = [0u];
            reader.DisksByVolume[FakeVolumeReader.SystemVolume] = [0u];
            var ops = new VirtualDiskStorageOps(runner, reader);

            ops.InitializeGpt(AcceptedDiskNumber);

            runner.Scripts.Count.ShouldBe(1);
            runner.Scripts[0].ShouldContain($"Initialize-Disk -Number {AcceptedDiskNumber} -PartitionStyle GPT");
        }
        finally
        {
            VirtualDiskRegistry.Unregister(AcceptedDiskNumber);
        }
    }

    /// <summary>Records every script instead of running it. Answers success with an empty output.</summary>
    private sealed class RecordingRunner : IPowerShellRunner
    {
        public List<string> Scripts { get; } = [];

        public PowerShellResult Run(string script)
        {
            Scripts.Add(script);
            return new PowerShellResult(0, string.Empty, string.Empty);
        }
    }

    /// <summary>Answers volume questions from fixed values, and counts every question it is asked.</summary>
    private sealed class FakeVolumeReader : IVolumeExtentsReader
    {
        public const string WindowsVolume = @"\\.\C:";
        public const string SystemVolume = @"\\?\GLOBALROOT\Device\HarddiskVolume1";

        public STORAGE_BUS_TYPE BusType { get; init; } = STORAGE_BUS_TYPE.BusTypeFileBackedVirtual;

        public Dictionary<string, uint[]> DisksByVolume { get; } = new(StringComparer.OrdinalIgnoreCase);

        public int Calls { get; private set; }

        public STORAGE_BUS_TYPE BusTypeOf(int diskNumber)
        {
            Calls++;
            return BusType;
        }

        public IReadOnlySet<uint> DiskNumbersOf(string volumePath)
        {
            Calls++;
            return new HashSet<uint>(DisksByVolume[volumePath]);
        }

        public string WindowsVolumePath()
        {
            Calls++;
            return WindowsVolume;
        }

        public string SystemPartitionVolumePath()
        {
            Calls++;
            return SystemVolume;
        }
    }
}
