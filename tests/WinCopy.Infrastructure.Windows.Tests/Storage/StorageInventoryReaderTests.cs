using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Platform;
using WinCopy.Infrastructure.Windows.Storage;
using Xunit;
using static WinCopy.Infrastructure.Windows.Tests.Storage.StorageRows;

namespace WinCopy.Infrastructure.Windows.Tests.Storage;

// The reader is run end to end with every OS dependency replaced by a fake. No WMI, device or registry is touched.
public sealed class StorageInventoryReaderTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 9, 30, 0, TimeSpan.Zero);

    private static readonly Guid EspVolume = new("00000000-0000-0000-0000-0000000000E1");
    private static readonly Guid WindowsVolume = new("1C5367FB-D302-425A-9F83-460D2490DE24");
    private static readonly Guid RecoveryVolume = new("2B35A440-D4BD-4BC4-A836-BCA5304E1A3B");
    private static readonly Guid DataVolume = new("00000000-0000-0000-0000-0000000000D5");

    private static readonly string EspPath = VolumePathOf(EspVolume);
    private static readonly string WindowsPath = VolumePathOf(WindowsVolume);
    private static readonly string RecoveryPath = VolumePathOf(RecoveryVolume);
    private static readonly string DataPath = VolumePathOf(DataVolume);

    private const string SystemPartitionPath = @"\\?\GLOBALROOT\Device\HarddiskVolume1";

    [Fact]
    public async Task RecordsFirmwareAndTheCaptureTime()
    {
        var rig = TypicalMachine();
        rig.Firmware = FirmwareType.Bios;

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        inventory.Firmware.ShouldBe(FirmwareType.Bios);
        inventory.CapturedAt.ShouldBe(Now);
    }

    [Fact]
    public async Task TheDiskHoldingTheWindowsVolumeIsTheSystemDisk()
    {
        DiskInventory inventory = await TypicalMachine().CreateReader().ReadAsync(CancellationToken.None);

        inventory.Disks.Single(d => d.Id.Number == 0).IsSystem.ShouldBeTrue();
        inventory.Disks.Single(d => d.Id.Number == 1).IsSystem.ShouldBeFalse();
        inventory.IsSupported.ShouldBeTrue();
        inventory.SystemDisk!.Id.Number.ShouldBe(0);
    }

    [Fact]
    public async Task ADiskHoldingOnlyTheBootEfiPartitionIsASystemDisk()
    {
        var rig = TypicalMachine();
        string bootEspPath = VolumePathOf(new Guid("00000000-0000-0000-0000-0000000000E2"));
        rig.Wmi.Disks.Add(GptDisk(2, new Guid("00000000-0000-0000-0000-00000000DD02")));
        rig.Wmi.Partitions[2] = [PartitionRow(1, 1 * Mib, 100 * Mib, EspType, new Guid("00000000-0000-0000-0000-0000000000A2"), bootEspPath)];
        rig.Wmi.Volumes[bootEspPath] = VolumeRow("FAT32", 100 * Mib, 60 * Mib);

        // The boot EFI partition now sits on disk 2 and Windows stays on disk 0.
        rig.Extents.Extents[SystemPartitionPath] = [2];

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        // Windows is still on disk 0, so both disks match the rule and the inventory is unsupported (not a silent pick).
        inventory.Disks.Single(d => d.Id.Number == 2).IsSystem.ShouldBeTrue();
        inventory.Disks.Single(d => d.Id.Number == 0).IsSystem.ShouldBeTrue();
        inventory.IsSupported.ShouldBeFalse();
    }

    [Fact]
    public async Task TheWindowsVolumeIsFlaggedByItsVolumeGuidPath()
    {
        DiskInventory inventory = await TypicalMachine().CreateReader().ReadAsync(CancellationToken.None);

        Partition[] partitions = inventory.Disks.Single(d => d.Id.Number == 0).Partitions.ToArray();

        partitions.Single(p => p.Number == 3).Volume!.IsWindowsVolume.ShouldBeTrue();
        partitions.Single(p => p.Number == 3).Role.ShouldBe(PartitionRole.Windows);
        partitions.Single(p => p.Number == 1).Volume!.IsWindowsVolume.ShouldBeFalse();
    }

    [Fact]
    public async Task BitLockerStateReachesEachVolume()
    {
        var rig = TypicalMachine();
        rig.BitLocker = new FakeBitLockerWmi(
        [
            new BitLockerVolumeRecord(WindowsPath.TrimEnd('\\').ToUpperInvariant(), 1, 1),
            new BitLockerVolumeRecord(EspPath, 0, 1),
            new BitLockerVolumeRecord(RecoveryPath, 0, 0),
            new BitLockerVolumeRecord(DataPath, 2, 0),
        ]);

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        Partition[] system = inventory.Disks.Single(d => d.Id.Number == 0).Partitions.ToArray();
        system.Single(p => p.Number == 3).Volume!.Encryption.ShouldBe(EncryptionState.BitLockerOn);
        system.Single(p => p.Number == 1).Volume!.Encryption.ShouldBe(EncryptionState.BitLockerSuspended);
        system.Single(p => p.Number == 4).Volume!.Encryption.ShouldBe(EncryptionState.None);
        inventory.Disks.Single(d => d.Id.Number == 1).Partitions.Single().Volume!.Encryption.ShouldBe(EncryptionState.Unknown);
    }

    [Fact]
    public async Task VolumesAreLookedUpOnlyForPartitionsThatHaveAVolumePath()
    {
        var rig = TypicalMachine();

        await rig.CreateReader().ReadAsync(CancellationToken.None);

        // Four partitions have a volume path (ESP, Windows, Recovery, data). The MSR has none, so it is never queried.
        rig.Wmi.VolumeQueries.ToArray().ShouldBe(new[] { EspPath, WindowsPath, RecoveryPath, DataPath });
    }

    [Fact]
    public async Task MsrHasNoVolume()
    {
        DiskInventory inventory = await TypicalMachine().CreateReader().ReadAsync(CancellationToken.None);

        Partition msr = inventory.Disks.Single(d => d.Id.Number == 0).Partitions.Single(p => p.Number == 2);

        msr.Role.ShouldBe(PartitionRole.MsReserved);
        msr.Volume.ShouldBeNull();
    }

    [Fact]
    public async Task AMissingVolumeRowLeavesTheVolumeNull()
    {
        var rig = TypicalMachine();
        rig.Wmi.Volumes.Remove(RecoveryPath);

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        inventory.Disks.Single(d => d.Id.Number == 0).Partitions.Single(p => p.Number == 4).Volume.ShouldBeNull();
    }

    [Fact]
    public async Task OnlyGptDisksAreReadForTheirLayout()
    {
        var rig = TypicalMachine();
        rig.Wmi.Disks.Add(new StorageDiskRecord(
            Number: 3, Guid: null, FriendlyName: "Card reader", SerialNumber: null, Size: 0,
            LogicalSectorSize: 512, PhysicalSectorSize: 512, BusType: BusUsb,
            PartitionStyle: StyleRaw, IsOffline: false, IsReadOnly: false));

        await rig.CreateReader().ReadAsync(CancellationToken.None);

        rig.Layout.Requested.OrderBy(n => n).ToArray().ShouldBe([0, 1]);
    }

    [Fact]
    public async Task LayoutAttributesAndNameReachThePartitions()
    {
        DiskInventory inventory = await TypicalMachine().CreateReader().ReadAsync(CancellationToken.None);

        Partition windows = inventory.Disks.Single(d => d.Id.Number == 0).Partitions.Single(p => p.Number == 3);

        windows.Attributes.ShouldBe(0x8000000000000001UL);
        windows.Name.ShouldBe("Basic data partition");
    }

    [Fact]
    public async Task ANonGptDiskKeepsItsPartitionsSoItIsNotReportedAsEmpty()
    {
        var rig = TypicalMachine();
        rig.Wmi.Disks.Add(new StorageDiskRecord(
            Number: 3, Guid: null, FriendlyName: "Old stick", SerialNumber: "OLD1", Size: 8 * Gib,
            LogicalSectorSize: 512, PhysicalSectorSize: 512, BusType: BusUsb,
            PartitionStyle: StyleMbr, IsOffline: false, IsReadOnly: false));
        rig.Wmi.Partitions[3] = [PartitionRow(1, 1 * Mib, 7 * Gib, null, null)];

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        Disk stick = inventory.Disks.Single(d => d.Id.Number == 3);
        stick.PartitionStyle.ShouldBe(PartitionStyle.Mbr);
        stick.Partitions.Count.ShouldBe(1);
        rig.Layout.Requested.ShouldNotContain(3);
    }

    [Fact]
    public async Task AGptLayoutFailureSkipsThatDiskWithAWarning()
    {
        var rig = TypicalMachine();
        rig.Layout.Failing.Add(1);

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        inventory.Disks.Select(d => d.Id.Number).ToArray().ShouldBe([0]);
        rig.Warnings.ShouldContain(w => w.StartsWith("Disk 1 skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ASectorSizeTheDomainCannotRepresentSkipsThatDiskWithAWarning()
    {
        var rig = TypicalMachine();
        rig.Wmi.Disks[1] = rig.Wmi.Disks[1] with { LogicalSectorSize = 2048 };

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        inventory.Disks.Select(d => d.Id.Number).ToArray().ShouldBe([0]);
        rig.Warnings.ShouldContain(w => w.StartsWith("Disk 1 skipped", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AStorageQueryFailureKeepsItsPlatformErrorCode()
    {
        var rig = TypicalMachine();
        rig.Wmi.DiskFailure = new PlatformException(PlatformErrorCode.DeviceNotFound, "MSFT_Disk is missing.");

        var ex = await Should.ThrowAsync<PlatformException>(() => rig.CreateReader().ReadAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.DeviceNotFound);
    }

    [Fact]
    public async Task AnUnexpectedStorageFailureBecomesAnUnknownPlatformError()
    {
        var rig = TypicalMachine();
        rig.Wmi.DiskFailure = new InvalidOperationException("WMI exploded");

        var ex = await Should.ThrowAsync<PlatformException>(() => rig.CreateReader().ReadAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.Unknown);
        ex.TechnicalDetail.ShouldContain("WMI exploded");
    }

    [Fact]
    public async Task UnresolvedExtentsFailTheReadInsteadOfGuessingTheSystemDisk()
    {
        var rig = TypicalMachine();
        rig.Extents.Extents.Remove(SystemPartitionPath);

        var ex = await Should.ThrowAsync<PlatformException>(() => rig.CreateReader().ReadAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.Unknown);
    }

    [Fact]
    public async Task AWindowsVolumeLookupFailureFailsTheRead()
    {
        var rig = TypicalMachine();
        rig.WindowsVolume = null;

        var ex = await Should.ThrowAsync<PlatformException>(() => rig.CreateReader().ReadAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.Unknown);
    }

    [Fact]
    public async Task ABitLockerNamespaceThatIsMissingReportsEveryVolumeAsNoneWithAWarning()
    {
        var rig = TypicalMachine();
        rig.BitLocker = new FakeBitLockerWmi(failure: new PlatformException(PlatformErrorCode.DeviceNotFound, "no namespace"));

        DiskInventory inventory = await rig.CreateReader().ReadAsync(CancellationToken.None);

        inventory.Disks.Single(d => d.Id.Number == 0).Partitions
            .Where(p => p.Volume is not null)
            .Select(p => p.Volume!.Encryption)
            .ToArray()
            .ShouldAllBe(state => state == EncryptionState.None);
        rig.Warnings.ShouldContain(w => w.Contains("BitLocker", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ABitLockerAccessDeniedFailsTheRead()
    {
        var rig = TypicalMachine();
        rig.BitLocker = new FakeBitLockerWmi(failure: new PlatformException(PlatformErrorCode.AccessDenied, "not admin"));

        var ex = await Should.ThrowAsync<PlatformException>(() => rig.CreateReader().ReadAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.AccessDenied);
    }

    [Fact]
    public void ACancelledReadThrowsBeforeAnyQuery()
    {
        var rig = TypicalMachine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Should.Throw<OperationCanceledException>(() => { _ = rig.CreateReader().ReadAsync(cts.Token); });

        rig.Wmi.VolumeQueries.ShouldBeEmpty();
    }

    // ---- the typical machine: GPT system disk (ESP, MSR, Windows, Recovery) plus a GPT USB data disk ----

    private static Rig TypicalMachine()
    {
        var rig = new Rig();

        rig.Wmi.Disks.Add(GptDisk(0, SystemDiskGuid));
        rig.Wmi.Disks.Add(GptDisk(1, UsbDiskGuid, bus: BusUsb));

        rig.Wmi.Partitions[0] =
        [
            PartitionRow(1, 1 * Mib, 100 * Mib, EspType, new Guid("00000000-0000-0000-0000-0000000000A1"), EspPath),
            PartitionRow(2, 101 * Mib, 16 * Mib, MsrType, new Guid("00000000-0000-0000-0000-0000000000A2")),
            PartitionRow(3, 117 * Mib, 900 * Gib, BasicType, new Guid("00000000-0000-0000-0000-0000000000A3"), WindowsPath, @"C:\"),
            PartitionRow(4, 117 * Mib + 900 * Gib, 1 * Gib, RecoveryType, new Guid("00000000-0000-0000-0000-0000000000A4"), RecoveryPath),
        ];
        rig.Wmi.Partitions[1] =
        [
            PartitionRow(1, 1 * Mib, 500 * Gib, BasicType, new Guid("00000000-0000-0000-0000-0000000000B1"), DataPath),
        ];

        rig.Wmi.Volumes[EspPath] = VolumeRow("FAT32", 100 * Mib, 60 * Mib);
        rig.Wmi.Volumes[WindowsPath] = VolumeRow("NTFS", 900 * Gib, 300 * Gib);
        rig.Wmi.Volumes[RecoveryPath] = VolumeRow("NTFS", 1 * Gib, 100 * Mib);
        rig.Wmi.Volumes[DataPath] = VolumeRow("NTFS", 500 * Gib, 400 * Gib);

        rig.Layout.Layouts[0] =
        [
            new DriveLayoutEntry(1, 0x0, "EFI system partition"),
            new DriveLayoutEntry(2, 0x8000000000000000UL, "Microsoft reserved partition"),
            new DriveLayoutEntry(3, 0x8000000000000001UL, "Basic data partition"),
            new DriveLayoutEntry(4, 0x8000000000000001UL, "Basic data partition"),
        ];
        rig.Layout.Layouts[1] = [new DriveLayoutEntry(1, 0, "Data")];

        rig.Extents.Extents[@"\\.\C:"] = [0];
        rig.Extents.Extents[SystemPartitionPath] = [0];

        // Every volume starts unencrypted, so a test only has to change the volume it cares about.
        rig.BitLocker = new FakeBitLockerWmi(
        [
            new BitLockerVolumeRecord(EspPath, 0, 0),
            new BitLockerVolumeRecord(WindowsPath, 0, 0),
            new BitLockerVolumeRecord(RecoveryPath, 0, 0),
            new BitLockerVolumeRecord(DataPath, 0, 0),
        ]);

        return rig;
    }

    /// <summary>Everything the reader depends on, each replaceable by a test.</summary>
    private sealed class Rig
    {
        public FakeStorageWmi Wmi { get; } = new();

        public FakeDriveLayoutReader Layout { get; } = new();

        public FakeVolumeExtentsReader Extents { get; } = new();

        public FakeBitLockerWmi BitLocker { get; set; } = new();

        public string? WindowsVolume { get; set; } = WindowsPath;

        public FirmwareType Firmware { get; set; } = FirmwareType.Uefi;

        public List<string> Warnings { get; } = [];

        public StorageInventoryReader CreateReader() => new(
            Wmi,
            Layout,
            new FakeFirmwareInfo(Firmware),
            new BitLockerQuery(BitLocker, Warnings.Add),
            new FakeWindowsVolumeInfo(WindowsVolume),
            Extents,
            new FixedClock(Now),
            Warnings.Add);
    }
}
