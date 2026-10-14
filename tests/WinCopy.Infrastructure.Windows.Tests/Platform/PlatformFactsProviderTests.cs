using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Platform;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Platform;

// Every OS dependency is a fake: power, services, volume paths and the lock probe. No volume is opened.
public sealed class PlatformFactsProviderTests
{
    private const long Mib = 1L << 20;
    private const long Gib = 1L << 30;

    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    // Disk 0 is the system disk. Disk 1 is a candidate target with data. Disk 2 is a candidate with only an MSR.
    private static readonly Guid SystemDiskGuid = new("702CA1D4-A415-4F7E-9D62-DA59090536B1");
    private static readonly Guid TargetDiskGuid = new("A1B2C3D4-0000-4000-8000-000000000001");
    private static readonly Guid EmptyTargetDiskGuid = new("A1B2C3D4-0000-4000-8000-000000000002");

    private static readonly Guid EspPartition = new("00000000-0000-0000-0000-0000000000A1");
    private static readonly Guid WindowsPartition = new("00000000-0000-0000-0000-0000000000A3");
    private static readonly Guid TargetDataPartition = new("00000000-0000-0000-0000-0000000000B1");
    private static readonly Guid TargetSecondPartition = new("00000000-0000-0000-0000-0000000000B2");
    private static readonly Guid MsrPartition = new("00000000-0000-0000-0000-0000000000C1");

    private static readonly string EspVolume = VolumeOf(new Guid("00000000-0000-0000-0000-0000000000E1"));
    private static readonly string WindowsVolume = VolumeOf(new Guid("1C5367FB-D302-425A-9F83-460D2490DE24"));
    private static readonly string TargetVolume = VolumeOf(new Guid("00000000-0000-0000-0000-0000000000D1"));
    private static readonly string TargetSecondVolume = VolumeOf(new Guid("00000000-0000-0000-0000-0000000000D2"));

    // ---- encryption ----

    [Fact]
    public async Task EncryptionCoversTheSourceVolumesOnly()
    {
        var facts = await Run(DefaultInventory());

        facts.Encryption.Count.ShouldBe(2);
        facts.Encryption[new PartitionGuid(EspPartition)].ShouldBe(EncryptionState.None);
        facts.Encryption[new PartitionGuid(WindowsPartition)].ShouldBe(EncryptionState.BitLockerOn);
        facts.Encryption.ContainsKey(new PartitionGuid(TargetDataPartition)).ShouldBeFalse();
        facts.Encryption.ContainsKey(new PartitionGuid(MsrPartition)).ShouldBeFalse();
    }

    [Fact]
    public async Task EncryptionIsEmptyWithoutASupportedSourceDisk()
    {
        var inventory = new DiskInventory(Now, FirmwareType.Uefi,
        [
            MakeDisk(1, TargetDiskGuid, isSystem: false, MakePart(1, TargetDataPartition, MakeVolume(encryption: EncryptionState.None))),
        ]);

        var facts = await Run(inventory);

        facts.Encryption.ShouldBeEmpty();
    }

    // ---- power ----

    [Fact]
    public async Task ReportsTheBatteryState()
    {
        var power = new FakePowerManager { Status = new PowerStatus(OnBattery: true, BatteryPercent: 42) };

        var facts = await Run(DefaultInventory(), power: power);

        facts.OnBattery.ShouldBeTrue();
        facts.BatteryPercent.ShouldBe(42);
    }

    [Fact]
    public async Task AnUnknownBatteryPercentStaysNull()
    {
        var power = new FakePowerManager { Status = new PowerStatus(OnBattery: false, BatteryPercent: null) };

        var facts = await Run(DefaultInventory(), power: power);

        facts.OnBattery.ShouldBeFalse();
        facts.BatteryPercent.ShouldBeNull();
    }

    [Fact]
    public async Task APowerReadFailureIsNotHidden()
    {
        var power = new FakePowerManager { Failure = new PlatformException(PlatformErrorCode.Unknown, "no power status") };

        var ex = await Should.ThrowAsync<PlatformException>(() => Run(DefaultInventory(), power: power));

        ex.Code.ShouldBe(PlatformErrorCode.Unknown);
    }

    // ---- VSS and snapshot space ----

    [Theory]
    [InlineData("Manual")]
    [InlineData("Automatic")]
    [InlineData("Boot")]
    [InlineData("System")]
    public async Task SnapshotsAreAvailableWhenVssIsInstalledAndNotDisabledOnNtfs(string startKind)
    {
        var services = new FakeServiceCatalog { Kind = Enum.Parse<ServiceStartKind>(startKind) };

        var facts = await Run(DefaultInventory(), services: services);

        facts.SnapshotAvailable.ShouldBeTrue();
        services.Asked.ShouldBe([PlatformFactsProvider.VssServiceName]);
    }

    [Fact]
    public async Task SnapshotsAreUnavailableWhenVssIsDisabled()
    {
        var facts = await Run(DefaultInventory(), services: new FakeServiceCatalog { Kind = ServiceStartKind.Disabled });

        facts.SnapshotAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task SnapshotsAreUnavailableWhenVssIsNotInstalled()
    {
        var facts = await Run(DefaultInventory(), services: new FakeServiceCatalog { Kind = null });

        facts.SnapshotAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task SnapshotsAreUnavailableWhenVssCannotBeQueried()
    {
        var services = new FakeServiceCatalog { Failure = new PlatformException(PlatformErrorCode.AccessDenied, "denied") };

        var facts = await Run(DefaultInventory(), services: services);

        facts.SnapshotAvailable.ShouldBeFalse();
    }

    [Theory]
    [InlineData(FileSystemKind.Fat32)]
    [InlineData(FileSystemKind.ExFat)]
    [InlineData(FileSystemKind.ReFs)]
    [InlineData(FileSystemKind.Raw)]
    public async Task SnapshotsNeedTheWindowsVolumeToBeNtfs(FileSystemKind fileSystem)
    {
        var inventory = DefaultInventory(windowsFileSystem: fileSystem);

        var facts = await Run(inventory, services: new FakeServiceCatalog { Kind = ServiceStartKind.Manual });

        facts.SnapshotAvailable.ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutAWindowsVolumeThereAreNoSnapshotsAndNoSpace()
    {
        var inventory = new DiskInventory(Now, FirmwareType.Uefi,
        [
            MakeDisk(0, SystemDiskGuid, isSystem: true, MakePart(1, EspPartition, MakeVolume(FileSystemKind.Fat32))),
        ]);

        var facts = await Run(inventory, services: new FakeServiceCatalog { Kind = ServiceStartKind.Manual });

        facts.SnapshotAvailable.ShouldBeFalse();
        facts.SnapshotFreeSpace.ShouldBe(new ByteSize(0));
    }

    [Fact]
    public async Task SnapshotFreeSpaceIsTheWindowsVolumeCapacityMinusItsUsedSpace()
    {
        var facts = await Run(DefaultInventory(windowsCapacity: 100 * Gib, windowsUsed: 40 * Gib));

        facts.SnapshotFreeSpace.ShouldBe(new ByteSize(60 * Gib));
    }

    [Fact]
    public async Task MinimumWindowsSizeIsNullUntilUs2()
    {
        var facts = await Run(DefaultInventory());

        facts.MinimumWindowsSize.ShouldBeNull();
    }

    // ---- TargetInUse ----

    [Fact]
    public async Task ATargetWhoseVolumeLocksIsNotInUse()
    {
        var facts = await Run(DefaultInventory());

        facts.TargetInUse[TargetId(1)].ShouldBeFalse();
    }

    [Fact]
    public async Task ATargetWhoseLockIsRefusedIsInUse()
    {
        var probe = new FakeVolumeLockProbe();
        probe.Results[WithoutTrailingSlash(TargetVolume)] = false;

        var facts = await Run(DefaultInventory(), probe: probe);

        facts.TargetInUse[TargetId(1)].ShouldBeTrue();
    }

    [Fact]
    public async Task ATargetWhoseProbeThrowsIsInUse()
    {
        var probe = new FakeVolumeLockProbe();
        probe.Throwing.Add(WithoutTrailingSlash(TargetVolume));

        var facts = await Run(DefaultInventory(), probe: probe);

        facts.TargetInUse[TargetId(1)].ShouldBeTrue();
    }

    [Fact]
    public async Task ATargetWhoseVolumePathCannotBeResolvedIsInUse()
    {
        var volumes = DefaultVolumePaths();
        volumes.Paths.Remove(new PartitionGuid(TargetDataPartition));

        var facts = await Run(DefaultInventory(), volumePaths: volumes);

        facts.TargetInUse[TargetId(1)].ShouldBeTrue();
    }

    [Fact]
    public async Task ADiskWithoutVolumesIsNotInUseAndIsNotProbed()
    {
        var probe = new FakeVolumeLockProbe();

        var facts = await Run(DefaultInventory(), probe: probe);

        facts.TargetInUse[TargetId(2)].ShouldBeFalse();

        // Only the data volume of disk 1 is probed. Disk 2 has no volume and the system disk is skipped.
        probe.Probed.Count.ShouldBe(1);
    }

    [Fact]
    public async Task TheSystemDiskIsNeverProbedAndHasNoEntry()
    {
        var probe = new FakeVolumeLockProbe();

        var facts = await Run(DefaultInventory(), probe: probe);

        facts.TargetInUse.ContainsKey(TargetId(0)).ShouldBeFalse();
        probe.Probed.ShouldNotContain(WithoutTrailingSlash(EspVolume));
        probe.Probed.ShouldNotContain(WithoutTrailingSlash(WindowsVolume));
    }

    [Fact]
    public async Task OneLockedVolumeMakesTheWholeDiskInUse()
    {
        var volumes = DefaultVolumePaths();
        volumes.Paths[new PartitionGuid(TargetSecondPartition)] = TargetSecondVolume;
        var probe = new FakeVolumeLockProbe();
        probe.Results[WithoutTrailingSlash(TargetSecondVolume)] = false;
        var inventory = new DiskInventory(Now, FirmwareType.Uefi,
        [
            MakeDisk(0, SystemDiskGuid, isSystem: true, MakePart(1, EspPartition, MakeVolume()), MakePart(3, WindowsPartition, MakeVolume(windows: true))),
            MakeDisk(1, TargetDiskGuid, isSystem: false,
                MakePart(1, TargetDataPartition, MakeVolume()),
                MakePart(2, TargetSecondPartition, MakeVolume())),
        ]);

        var facts = await Run(inventory, probe: probe, volumePaths: volumes);

        facts.TargetInUse[TargetId(1)].ShouldBeTrue();
        probe.Probed.ShouldContain(WithoutTrailingSlash(TargetVolume));
        probe.Probed.ShouldContain(WithoutTrailingSlash(TargetSecondVolume));
    }

    [Fact]
    public async Task TheProbeReceivesTheVolumeDevicePathWithoutTheTrailingBackslash()
    {
        var probe = new FakeVolumeLockProbe();

        await Run(DefaultInventory(), probe: probe);

        probe.Probed.ToArray().ShouldBe(new[] { WithoutTrailingSlash(TargetVolume) });
        probe.Probed.ShouldAllBe(path => !path.EndsWith('\\'));
    }

    [Fact]
    public async Task NonGptDisksWithTheEmptyGuidKeepSeparateResults()
    {
        // Two non-GPT disks both have Guid.Empty; DiskId then falls back to the disk number, so each disk keeps its own
        // in-use result and one disk's state can never be attributed to the other.
        var volumes = new FakeVolumePaths();
        volumes.Paths[new PartitionGuid(TargetDataPartition)] = TargetVolume;
        volumes.Paths[new PartitionGuid(TargetSecondPartition)] = TargetSecondVolume;
        var probe = new FakeVolumeLockProbe();
        probe.Results[WithoutTrailingSlash(TargetSecondVolume)] = false;
        var inventory = new DiskInventory(Now, FirmwareType.Uefi,
        [
            MakeDisk(0, SystemDiskGuid, isSystem: true, MakePart(1, EspPartition, MakeVolume())),
            MakeDisk(5, Guid.Empty, isSystem: false, MakePart(1, TargetDataPartition, MakeVolume())),
            MakeDisk(6, Guid.Empty, isSystem: false, MakePart(1, TargetSecondPartition, MakeVolume())),
        ]);

        var facts = await Run(inventory, probe: probe, volumePaths: volumes);

        facts.TargetInUse.Count(pair => pair.Key.DiskGuid == Guid.Empty).ShouldBe(2);
        facts.TargetInUse[new DiskId(5, Guid.Empty)].ShouldBeFalse();
        facts.TargetInUse[new DiskId(6, Guid.Empty)].ShouldBeTrue();
    }

    [Fact]
    public async Task ACancelledRequestThrows()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            Provider().GetAsync(DefaultInventory(), cts.Token));
    }

    // ---- helpers ----

    private static PlatformFactsProvider Provider(
        FakePowerManager? power = null,
        FakeServiceCatalog? services = null,
        FakeVolumePaths? volumePaths = null,
        FakeVolumeLockProbe? probe = null) =>
        new(
            power ?? new FakePowerManager(),
            services ?? new FakeServiceCatalog { Kind = ServiceStartKind.Manual },
            volumePaths ?? DefaultVolumePaths(),
            probe ?? new FakeVolumeLockProbe());

    private static Task<PlatformFacts> Run(
        DiskInventory inventory,
        FakeVolumePaths? volumePaths = null,
        FakePowerManager? power = null,
        FakeServiceCatalog? services = null,
        FakeVolumeLockProbe? probe = null) =>
        Provider(power, services, volumePaths, probe).GetAsync(inventory, CancellationToken.None);

    private static FakeVolumePaths DefaultVolumePaths()
    {
        var volumes = new FakeVolumePaths();
        volumes.Paths[new PartitionGuid(EspPartition)] = EspVolume;
        volumes.Paths[new PartitionGuid(WindowsPartition)] = WindowsVolume;
        volumes.Paths[new PartitionGuid(TargetDataPartition)] = TargetVolume;
        return volumes;
    }

    /// <summary>
    /// The default inventory: a system disk with ESP and Windows, a target with data, and a target with only an MSR.
    /// The Windows volume is BitLocker-on and the ESP is unencrypted.
    /// </summary>
    private static DiskInventory DefaultInventory(
        FileSystemKind windowsFileSystem = FileSystemKind.Ntfs,
        long windowsCapacity = 100 * Gib,
        long windowsUsed = 40 * Gib) =>
        new(Now, FirmwareType.Uefi,
        [
            MakeDisk(0, SystemDiskGuid, isSystem: true,
                MakePart(1, EspPartition, MakeVolume(FileSystemKind.Fat32, encryption: EncryptionState.None)),
                MakePart(2, MsrPartition, volume: null, type: GptPartitionType.MicrosoftReserved),
                MakePart(3, WindowsPartition, MakeVolume(windowsFileSystem, windowsCapacity, windowsUsed, EncryptionState.BitLockerOn, windows: true))),
            MakeDisk(1, TargetDiskGuid, isSystem: false,
                MakePart(1, TargetDataPartition, MakeVolume(encryption: EncryptionState.None))),
            MakeDisk(2, EmptyTargetDiskGuid, isSystem: false,
                MakePart(1, MsrPartition, volume: null, type: GptPartitionType.MicrosoftReserved)),
        ]);

    private static Disk MakeDisk(int number, Guid guid, bool isSystem, params Partition[] partitions) =>
        new(
            new DiskId(number, guid),
            "Test disk",
            "SERIAL",
            new ByteSize(500 * Gib),
            SectorSize.Bytes512,
            SectorSize.Bytes4096,
            BusKind.Nvme,
            PartitionStyle.Gpt,
            isSystem,
            partitions,
            IsOffline: false,
            IsReadOnly: false);

    private static Partition MakePart(int number, Guid partitionGuid, Volume? volume, GptPartitionType? type = null) =>
        new(
            number,
            new Extent(number * Mib, Mib),
            type ?? GptPartitionType.BasicData,
            new PartitionGuid(partitionGuid),
            0,
            string.Empty,
            volume);

    private static Volume MakeVolume(
        FileSystemKind fileSystem = FileSystemKind.Ntfs,
        long capacity = 100 * Gib,
        long used = 40 * Gib,
        EncryptionState encryption = EncryptionState.None,
        bool windows = false) =>
        new(fileSystem, new ByteSize(capacity), new ByteSize(used), new ByteSize(4096), encryption, windows, null);

    private static DiskId TargetId(int number) => number switch
    {
        0 => new DiskId(0, SystemDiskGuid),
        1 => new DiskId(1, TargetDiskGuid),
        _ => new DiskId(2, EmptyTargetDiskGuid),
    };

    private static string VolumeOf(Guid volumeGuid) => @"\\?\Volume" + volumeGuid.ToString("B") + @"\";

    private static string WithoutTrailingSlash(string volumePath) => volumePath.TrimEnd('\\');
}
