using System.Reflection;
using System.Runtime.InteropServices;
using Shouldly;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

// Tests of the physical-disk protection. Only the system-disk checks read real metadata (desiredAccess 0), and only
// the fixture test creates or attaches a disk, so it is the only test that needs an elevated process. Every other
// test uses fakes, and no fake opener ever reaches a real device.
[Trait("Category", "Integration")]
public sealed class PhysicalDiskGuardTests
{
    // Disk numbers that exist only inside these tests.
    private const int FakeVirtualDiskNumber = 77;
    private const int FakeNvmeDiskNumber = 99;

    [Fact]
    public void RestrictionIsActiveAtStartup()
    {
        DiskWriteGuard.Default.RestrictionNames.ShouldContain(VirtualDisksOnlyRestriction.RestrictionName);
    }

    [Fact]
    public void RejectsSystemDisk()
    {
        int systemDisk = SystemDiskNumber();

        DiskWriteDeniedException ex = Should.Throw<DiskWriteDeniedException>(
            () => DiskWriteGuard.Default.OpenDiskForWrite(systemDisk).Dispose());

        ex.DiskNumber.ShouldBe(systemDisk);
        ex.Reason.ShouldContain("system disk");
    }

    [Fact]
    public void AdapterCreatedDirectlyOnPhysicalDiskIsRejected()
    {
        int systemDisk = SystemDiskNumber();
        var opener = new RecordingOpener();

        // Real probe, so the system disk is identified from the machine itself. The fake opener makes sure a
        // missed check could not write to a real disk.
        var guard = new DiskWriteGuard(new WindowsDiskIdentityProbe(), opener);
        guard.AddRestriction(new VirtualDisksOnlyRestriction());

        DiskWriteDeniedException systemDenial = Should.Throw<DiskWriteDeniedException>(
            () => guard.OpenDiskForWrite(systemDisk).Dispose());

        systemDenial.DiskNumber.ShouldBe(systemDisk);
        systemDenial.Reason.ShouldContain("system disk");
        opener.OpenedPaths.ShouldBeEmpty();

        // A disk that is neither system nor virtual. Only the restriction can deny it.
        var nvmeProbe = new FakeProbe(new DiskWriteCandidate(
            FakeNvmeDiskNumber,
            STORAGE_BUS_TYPE.BusTypeNvme,
            IsSystemDisk: false,
            ContainsRunningWindows: false));
        var nvmeGuard = new DiskWriteGuard(nvmeProbe, opener);
        nvmeGuard.AddRestriction(new VirtualDisksOnlyRestriction());

        DiskWriteDeniedException nvmeDenial = Should.Throw<DiskWriteDeniedException>(
            () => nvmeGuard.OpenDiskForWrite(FakeNvmeDiskNumber).Dispose());

        nvmeDenial.Message.ShouldContain(VirtualDisksOnlyRestriction.RestrictionName);
        opener.OpenedPaths.ShouldBeEmpty();
    }

    [Fact]
    public void VirtualDiskIsAllowedByRestriction()
    {
        var probe = new FakeProbe(new DiskWriteCandidate(
            FakeVirtualDiskNumber,
            STORAGE_BUS_TYPE.BusTypeFileBackedVirtual,
            IsSystemDisk: false,
            ContainsRunningWindows: false));
        var opener = new RecordingOpener();
        var guard = new DiskWriteGuard(probe, opener);
        guard.AddRestriction(new VirtualDisksOnlyRestriction());

        // Not registered by a fixture, so the restriction denies it.
        DiskWriteDeniedException denial = Should.Throw<DiskWriteDeniedException>(
            () => guard.OpenDiskForWrite(FakeVirtualDiskNumber).Dispose());

        denial.Message.ShouldContain(VirtualDisksOnlyRestriction.RestrictionName);
        opener.OpenedPaths.ShouldBeEmpty();

        VirtualDiskRegistry.Register(FakeVirtualDiskNumber);
        try
        {
            using SafeDeviceHandle handle = guard.OpenDiskForWrite(FakeVirtualDiskNumber);

            handle.IsInvalid.ShouldBeFalse();
            opener.OpenedPaths.ShouldBe([@"\\.\PhysicalDrive77"]);
        }
        finally
        {
            VirtualDiskRegistry.Unregister(FakeVirtualDiskNumber);
        }
    }

    [Fact]
    public async Task FixtureCanCreateAttachAndDetach()
    {
        var fixture = new VirtualDiskFixture();
        int diskNumber = -1;
        string vhdxPath = string.Empty;

        try
        {
            AttachedVirtualDisk disk = await fixture.CreateAndAttachAsync(ByteSize.FromMiB(64));
            diskNumber = disk.DiskNumber;
            vhdxPath = disk.VhdxPath;

            diskNumber.ShouldBeGreaterThanOrEqualTo(0);
            VirtualDiskRegistry.Contains(diskNumber).ShouldBeTrue();
            File.Exists(vhdxPath).ShouldBeTrue();
        }
        finally
        {
            await fixture.DisposeAsync();
        }

        VirtualDiskRegistry.Contains(diskNumber).ShouldBeFalse();
        File.Exists(vhdxPath).ShouldBeFalse();
        Directory.Exists(fixture.RootFolder).ShouldBeFalse();
    }

    /// <summary>Disk that holds the running Windows volume, read with desiredAccess 0.</summary>
    private static int SystemDiskNumber()
    {
        var reader = new WindowsVolumeExtentsReader();
        IReadOnlySet<uint> diskNumbers = reader.DiskNumbersOf(reader.WindowsVolumePath());
        return (int)diskNumbers.Min();
    }

    /// <summary>Returns a fixed candidate for any disk number.</summary>
    private sealed class FakeProbe(DiskWriteCandidate candidate) : IDiskIdentityProbe
    {
        public DiskWriteCandidate Probe(int diskNumber) => candidate;
    }

    /// <summary>Records the paths it was asked to open and returns an inert handle.</summary>
    private sealed class RecordingOpener : IDeviceOpener
    {
        public List<string> OpenedPaths { get; } = [];

        public SafeDeviceHandle OpenForReadWrite(string devicePath)
        {
            OpenedPaths.Add(devicePath);
            return InertHandle();
        }

        /// <summary>
        /// Wraps an unnamed kernel event in a SafeDeviceHandle. Ownership moves from the EventWaitHandle to the
        /// returned handle, so disposing the result closes the event and nothing else.
        /// </summary>
        private static SafeDeviceHandle InertHandle()
        {
            using var waitHandle = new EventWaitHandle(false, EventResetMode.ManualReset);
            IntPtr eventHandle = waitHandle.SafeWaitHandle.DangerousGetHandle();
            waitHandle.SafeWaitHandle.SetHandleAsInvalid();

            var handle = new SafeDeviceHandle();
            SetHandleMethod.Invoke(handle, [eventHandle]);
            return handle;
        }

        // SafeHandle.SetHandle is protected and SafeDeviceHandle is sealed, so a test can only set the value by reflection.
        private static readonly MethodInfo SetHandleMethod =
            typeof(SafeHandle).GetMethod("SetHandle", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SafeHandle.SetHandle not found.");
    }
}
