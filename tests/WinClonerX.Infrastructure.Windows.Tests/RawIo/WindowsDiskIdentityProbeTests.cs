using Shouldly;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// The probe is tested through a fake reader. No test opens a device, a volume or the registry.
public sealed class WindowsDiskIdentityProbeTests
{
    private const string WindowsVolume = @"\\.\C:";
    private const string SystemVolume = @"\\?\GLOBALROOT\Device\HarddiskVolume1";

    [Fact]
    public void DiskWithOnlyTheEfiSystemPartitionIsSystemButNotRunningWindows()
    {
        var reader = new FakeReader(windowsDisks: [0u], systemPartitionDisks: [1u]);
        var probe = new WindowsDiskIdentityProbe(reader);

        DiskWriteCandidate candidate = probe.Probe(1);

        candidate.IsSystemDisk.ShouldBeTrue();
        candidate.ContainsRunningWindows.ShouldBeFalse();
    }

    [Fact]
    public void DiskWithWindowsIsSystemAndRunningWindows()
    {
        var reader = new FakeReader(windowsDisks: [0u], systemPartitionDisks: [1u]);
        var probe = new WindowsDiskIdentityProbe(reader);

        DiskWriteCandidate candidate = probe.Probe(0);

        candidate.IsSystemDisk.ShouldBeTrue();
        candidate.ContainsRunningWindows.ShouldBeTrue();
    }

    [Fact]
    public void DiskWithWindowsAndEfiOnTheSameDiskIsSystemAndRunningWindows()
    {
        var reader = new FakeReader(windowsDisks: [0u], systemPartitionDisks: [0u]);
        var probe = new WindowsDiskIdentityProbe(reader);

        DiskWriteCandidate candidate = probe.Probe(0);

        candidate.IsSystemDisk.ShouldBeTrue();
        candidate.ContainsRunningWindows.ShouldBeTrue();
    }

    [Fact]
    public void UnrelatedDiskIsNeitherSystemNorRunningWindows()
    {
        var reader = new FakeReader(windowsDisks: [0u], systemPartitionDisks: [1u]);
        var probe = new WindowsDiskIdentityProbe(reader);

        DiskWriteCandidate candidate = probe.Probe(2);

        candidate.DiskNumber.ShouldBe(2);
        candidate.BusType.ShouldBe(STORAGE_BUS_TYPE.BusTypeNvme);
        candidate.IsSystemDisk.ShouldBeFalse();
        candidate.ContainsRunningWindows.ShouldBeFalse();
    }

    [Fact]
    public void FailureReadingTheSystemPartitionMakesTheProbeThrow()
    {
        var reader = new FakeReader(windowsDisks: [0u], systemPartitionDisks: [1u]) { SystemPartitionFails = true };
        var probe = new WindowsDiskIdentityProbe(reader);

        Should.Throw<InvalidOperationException>(() => probe.Probe(2));
    }

    [Fact]
    public void GuardDeniesDiskThatOnlyHostsTheEfiSystemPartition()
    {
        var reader = new FakeReader(windowsDisks: [0u], systemPartitionDisks: [1u]);
        var opener = new ForbiddenOpener();
        var guard = new DiskWriteGuard(new WindowsDiskIdentityProbe(reader), opener);

        var ex = Should.Throw<DiskWriteDeniedException>(() => guard.OpenDiskForWrite(1));

        ex.DiskNumber.ShouldBe(1);
        opener.CallCount.ShouldBe(0);
    }

    /// <summary>Answers volume and disk questions from fixed sets. Never touches a device or the registry.</summary>
    private sealed class FakeReader(IEnumerable<uint> windowsDisks, IEnumerable<uint> systemPartitionDisks)
        : IVolumeExtentsReader
    {
        private readonly HashSet<uint> _windowsDisks = [.. windowsDisks];
        private readonly HashSet<uint> _systemPartitionDisks = [.. systemPartitionDisks];

        public bool SystemPartitionFails { get; init; }

        public IReadOnlySet<uint> DiskNumbersOf(string volumePath) => volumePath switch
        {
            WindowsVolume => _windowsDisks,
            SystemVolume => _systemPartitionDisks,
            _ => throw new InvalidOperationException($"Unexpected volume '{volumePath}' in test."),
        };

        public string WindowsVolumePath() => WindowsVolume;

        public string SystemPartitionVolumePath() => SystemPartitionFails
            ? throw new InvalidOperationException("SystemPartition is missing in test.")
            : SystemVolume;

        public STORAGE_BUS_TYPE BusTypeOf(int diskNumber) => STORAGE_BUS_TYPE.BusTypeNvme;
    }

    /// <summary>Fails the test if the guard ever reaches the opener.</summary>
    private sealed class ForbiddenOpener : IDeviceOpener
    {
        public int CallCount { get; private set; }

        public SafeDeviceHandle OpenForReadWrite(string devicePath)
        {
            CallCount++;
            throw new InvalidOperationException("The opener must not be called in this test.");
        }
    }
}
