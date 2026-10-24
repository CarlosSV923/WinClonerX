using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Storage;
using Xunit;
using static WinClonerX.Infrastructure.Windows.Tests.Storage.StorageRows;

namespace WinClonerX.Infrastructure.Windows.Tests.Storage;

// Pure mapping from raw Storage API rows to domain objects. No device, WMI or registry is touched.
public sealed class StorageInventoryMapperTests
{
    private static readonly Guid DiskGuid = new("702CA1D4-A415-4F7E-9D62-DA59090536B1");
    private static readonly string WindowsVolume = VolumePathOf(new Guid("1C5367FB-D302-425A-9F83-460D2490DE24"));
    private static readonly int[] ExpectedNumbers = [1, 2];

    [Theory]
    [InlineData((ushort)7, BusKind.Usb)]
    [InlineData((ushort)8, BusKind.Raid)]
    [InlineData((ushort)10, BusKind.Sas)]
    [InlineData((ushort)11, BusKind.Sata)]
    [InlineData((ushort)15, BusKind.FileBackedVirtual)]
    [InlineData((ushort)16, BusKind.Spaces)]
    [InlineData((ushort)17, BusKind.Nvme)]
    [InlineData((ushort)0, BusKind.Other)]
    [InlineData((ushort)3, BusKind.Other)]
    [InlineData((ushort)14, BusKind.Other)]
    [InlineData((ushort)999, BusKind.Other)]
    public void MapsBusTypeCodes(ushort code, BusKind expected) =>
        StorageInventoryMapper.MapBus(code).ShouldBe(expected);

    [Theory]
    [InlineData((ushort)0, PartitionStyle.Raw)]
    [InlineData((ushort)1, PartitionStyle.Mbr)]
    [InlineData((ushort)2, PartitionStyle.Gpt)]
    [InlineData((ushort)9, PartitionStyle.Raw)]
    public void MapsPartitionStyleCodes(ushort code, PartitionStyle expected) =>
        StorageInventoryMapper.MapPartitionStyle(code).ShouldBe(expected);

    [Theory]
    [InlineData("NTFS", FileSystemKind.Ntfs)]
    [InlineData("ntfs", FileSystemKind.Ntfs)]
    [InlineData("FAT32", FileSystemKind.Fat32)]
    [InlineData("exFAT", FileSystemKind.ExFat)]
    [InlineData("ReFS", FileSystemKind.ReFs)]
    [InlineData("RAW", FileSystemKind.Raw)]
    [InlineData("", FileSystemKind.Raw)]
    [InlineData(null, FileSystemKind.Raw)]
    [InlineData("UDF", FileSystemKind.Other)]
    public void MapsFileSystemNames(string? name, FileSystemKind expected) =>
        StorageInventoryMapper.MapFileSystem(name).ShouldBe(expected);

    [Fact]
    public void VolumeUsedIsSizeMinusRemaining()
    {
        var volume = StorageInventoryMapper.BuildVolume(
            VolumeRow("NTFS", size: 100 * Gib, sizeRemaining: 40 * Gib), isWindowsVolume: false, EncryptionState.None);

        volume.Capacity.ShouldBe(new ByteSize((long)(100 * Gib)));
        volume.Used.ShouldBe(new ByteSize((long)(60 * Gib)));
    }

    [Fact]
    public void VolumeClusterSizeComesFromAllocationUnit()
    {
        var volume = StorageInventoryMapper.BuildVolume(
            VolumeRow("NTFS", 1 * Gib, 1 * Gib, allocationUnit: 65536), isWindowsVolume: false, EncryptionState.None);

        volume.ClusterSize.ShouldBe(new ByteSize(65536));
    }

    [Fact]
    public void ZeroAllocationUnitMeansNoClusterSize()
    {
        var volume = StorageInventoryMapper.BuildVolume(
            VolumeRow("RAW", 1 * Gib, 1 * Gib, allocationUnit: 0), isWindowsVolume: false, EncryptionState.None);

        volume.ClusterSize.ShouldBeNull();
    }

    [Fact]
    public void RemainingAboveSizeReadsAsZeroUsedSoTheDomainNeverSeesUsedAboveCapacity()
    {
        var volume = StorageInventoryMapper.BuildVolume(
            VolumeRow("NTFS", 1 * Gib, 2 * Gib), isWindowsVolume: false, EncryptionState.None);

        volume.Used.ShouldBe(new ByteSize(0));
    }

    [Fact]
    public void VolumeCarriesTheWindowsFlagEncryptionAndNoInUseFact()
    {
        var volume = StorageInventoryMapper.BuildVolume(
            VolumeRow("NTFS", 1 * Gib, 0), isWindowsVolume: true, EncryptionState.BitLockerSuspended);

        volume.IsWindowsVolume.ShouldBeTrue();
        volume.Encryption.ShouldBe(EncryptionState.BitLockerSuspended);
        volume.InUseByOthers.ShouldBeNull();
    }

    [Fact]
    public void SyntheticPartitionGuidIsStableForOneLayout()
    {
        var first = StorageInventoryMapper.SyntheticPartitionGuid(2, 1, 1 * Mib, 100 * Mib);
        var second = StorageInventoryMapper.SyntheticPartitionGuid(2, 1, 1 * Mib, 100 * Mib);

        first.ShouldBe(second);
        first.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public void SyntheticPartitionGuidDiffersBetweenDisksPartitionsAndOffsets()
    {
        var baseline = StorageInventoryMapper.SyntheticPartitionGuid(2, 1, 1 * Mib, 100 * Mib);

        StorageInventoryMapper.SyntheticPartitionGuid(3, 1, 1 * Mib, 100 * Mib).ShouldNotBe(baseline);
        StorageInventoryMapper.SyntheticPartitionGuid(2, 2, 1 * Mib, 100 * Mib).ShouldNotBe(baseline);
        StorageInventoryMapper.SyntheticPartitionGuid(2, 1, 2 * Mib, 100 * Mib).ShouldNotBe(baseline);
    }

    [Fact]
    public void BuildsTheDiskFromItsStorageRow()
    {
        var disk = GptDisk(0, DiskGuid, bus: BusNvme, serial: "  0025_3857  ") with
        {
            FriendlyName = "  Samsung SSD 990  ",
            IsOffline = true,
            IsReadOnly = true,
        };

        Disk built = StorageInventoryMapper.BuildDisk(Snapshot(disk), isSystem: true, WindowsVolume, NoEncryption());

        built.Id.ShouldBe(new DiskId(0, DiskGuid));
        built.Model.ShouldBe("Samsung SSD 990");
        built.SerialNumber.ShouldBe("0025_3857");
        built.Size.ShouldBe(new ByteSize((long)(1000 * Gib)));
        built.LogicalSectorSize.ShouldBe(SectorSize.Bytes512);
        built.PhysicalSectorSize.ShouldBe(SectorSize.Bytes4096);
        built.Bus.ShouldBe(BusKind.Nvme);
        built.PartitionStyle.ShouldBe(PartitionStyle.Gpt);
        built.IsSystem.ShouldBeTrue();
        built.IsOffline.ShouldBeTrue();
        built.IsReadOnly.ShouldBeTrue();
    }

    [Fact]
    public void GptPartitionRolesFollowTheGptTypeAndTheWindowsVolume()
    {
        var snapshot = Snapshot(
            GptDisk(0, DiskGuid),
            PartSnap(1, 1 * Mib, 100 * Mib, EspType, 0x11, VolumePathOf(GuidOf(0x51)), VolumeRow("FAT32", 100 * Mib, 60 * Mib)),
            PartSnap(2, 101 * Mib, 16 * Mib, MsrType, 0x22),
            PartSnap(3, 117 * Mib, 900 * Gib, BasicType, 0x33, WindowsVolume, VolumeRow("NTFS", 900 * Gib, 300 * Gib)),
            PartSnap(4, 900 * Gib + 117 * Mib, 1 * Gib, RecoveryType, 0x55, VolumePathOf(GuidOf(0x66)), VolumeRow("NTFS", 1 * Gib, 0)),
            PartSnap(5, 901 * Gib + 117 * Mib, 1 * Gib, UnknownType, 0x77, VolumePathOf(GuidOf(0x88)), VolumeRow("NTFS", 1 * Gib, 0)));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: true, WindowsVolume, NoEncryption());

        built.Partitions.Select(p => p.Role).ToArray().ShouldBe(new[]
        {
            PartitionRole.EfiSystem,
            PartitionRole.MsReserved,
            PartitionRole.Windows,
            PartitionRole.Recovery,
            PartitionRole.Unsupported,
        });
    }

    [Fact]
    public void BasicDataWithoutTheWindowsVolumeIsData()
    {
        var snapshot = Snapshot(
            GptDisk(1, UsbDiskGuid, bus: BusUsb),
            PartSnap(1, 1 * Mib, 500 * Gib, BasicType, 0x99, VolumePathOf(GuidOf(0xAA)), VolumeRow("NTFS", 500 * Gib, 400 * Gib)));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: false, WindowsVolume, NoEncryption());

        built.Partitions[0].Role.ShouldBe(PartitionRole.Data);
        built.Bus.ShouldBe(BusKind.Usb);
    }

    [Fact]
    public void MsrPartitionWithoutAVolumeHasNoVolume()
    {
        var snapshot = Snapshot(GptDisk(0, DiskGuid), PartSnap(1, 1 * Mib, 16 * Mib, MsrType, 0x22));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: true, WindowsVolume, NoEncryption());

        built.Partitions[0].Volume.ShouldBeNull();
        built.Partitions[0].Role.ShouldBe(PartitionRole.MsReserved);
    }

    [Fact]
    public void PartitionWithAVolumeRecordButNoVolumePathHasNoVolume()
    {
        var snapshot = Snapshot(
            GptDisk(0, DiskGuid),
            new PartitionSnapshot(
                PartitionRow(1, 1 * Mib, 10 * Mib, BasicType, GuidOf(0x33)),
                VolumeGuidPath: null,
                Volume: VolumeRow("NTFS", 10 * Mib, 0)));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: false, WindowsVolume, NoEncryption());

        built.Partitions[0].Volume.ShouldBeNull();
    }

    [Fact]
    public void WindowsVolumeMatchesIgnoringTrailingBackslashAndCase()
    {
        string withoutSlashUpper = WindowsVolume.TrimEnd('\\').ToUpperInvariant();
        var snapshot = Snapshot(
            GptDisk(0, DiskGuid),
            PartSnap(1, 1 * Mib, 10 * Gib, BasicType, 0x33, WindowsVolume, VolumeRow("NTFS", 10 * Gib, 0)));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: true, withoutSlashUpper, NoEncryption());

        built.Partitions[0].Volume!.IsWindowsVolume.ShouldBeTrue();
        built.Partitions[0].Role.ShouldBe(PartitionRole.Windows);
    }

    [Fact]
    public void AVolumeThatIsNotTheWindowsVolumeIsNotFlagged()
    {
        var snapshot = Snapshot(
            GptDisk(0, DiskGuid),
            PartSnap(1, 1 * Mib, 10 * Gib, BasicType, 0x33, VolumePathOf(GuidOf(0x44)), VolumeRow("NTFS", 10 * Gib, 0)));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: true, WindowsVolume, NoEncryption());

        built.Partitions[0].Volume!.IsWindowsVolume.ShouldBeFalse();
    }

    [Fact]
    public void AttributesAndNameAreMergedFromTheLayoutByPartitionNumber()
    {
        var snapshot = new DiskSnapshot(
            GptDisk(0, DiskGuid),
            [
                PartSnap(2, 2 * Mib, 16 * Mib, MsrType, 0x22),
                PartSnap(1, 1 * Mib, 100 * Mib, EspType, 0x11),
            ],
            [
                new DriveLayoutEntry(1, 0x0, "EFI system partition"),
                new DriveLayoutEntry(2, 0x8000000000000000UL, "Microsoft reserved partition"),
            ]);

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: true, WindowsVolume, NoEncryption());

        built.Partitions.Select(p => (p.Number, p.Attributes, p.Name)).ToArray().ShouldBe(new[]
        {
            (1, 0UL, "EFI system partition"),
            (2, 0x8000000000000000UL, "Microsoft reserved partition"),
        });
    }

    [Fact]
    public void AMissingLayoutEntryReadsAsZeroAttributesAndEmptyName()
    {
        var snapshot = new DiskSnapshot(
            GptDisk(0, DiskGuid),
            [PartSnap(1, 1 * Mib, 16 * Mib, MsrType, 0x22)],
            []);

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: true, WindowsVolume, NoEncryption());

        built.Partitions[0].Attributes.ShouldBe(0UL);
        built.Partitions[0].Name.ShouldBe(string.Empty);
    }

    [Fact]
    public void PartitionsAreSortedByOffset()
    {
        var snapshot = Snapshot(
            GptDisk(0, DiskGuid),
            PartSnap(2, 500 * Mib, 1 * Mib, BasicType, 0x22),
            PartSnap(1, 1 * Mib, 1 * Mib, BasicType, 0x11));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: false, WindowsVolume, NoEncryption());

        built.Partitions.Select(p => p.Number).ToArray().ShouldBe(ExpectedNumbers);
    }

    [Fact]
    public void EncryptionComesFromTheDictionaryAndAMissingKeyIsUnknown()
    {
        string known = VolumePathOf(GuidOf(0x44));
        string unknown = VolumePathOf(GuidOf(0x66));
        var snapshot = Snapshot(
            GptDisk(0, DiskGuid),
            PartSnap(1, 1 * Mib, 10 * Gib, BasicType, 0x33, known, VolumeRow("NTFS", 10 * Gib, 0)),
            PartSnap(2, 11 * Gib, 10 * Gib, BasicType, 0x34, unknown, VolumeRow("NTFS", 10 * Gib, 0)));
        var encryption = new Dictionary<string, EncryptionState>(StringComparer.Ordinal)
        {
            [VolumePath.Normalize(known)] = EncryptionState.BitLockerOn,
        };

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: false, WindowsVolume, encryption);

        built.Partitions[0].Volume!.Encryption.ShouldBe(EncryptionState.BitLockerOn);
        built.Partitions[1].Volume!.Encryption.ShouldBe(EncryptionState.Unknown);
    }

    [Fact]
    public void NonGptDiskWithoutAGuidUsesGuidEmptyAndKeepsItsPartitions()
    {
        // The evaluator reads a target with partitions as "not empty", so MBR partitions must stay in the inventory.
        var disk = new StorageDiskRecord(
            Number: 4, Guid: null, FriendlyName: "USB stick", SerialNumber: null, Size: 16 * Gib,
            LogicalSectorSize: 512, PhysicalSectorSize: 512, BusType: BusUsb,
            PartitionStyle: StyleMbr, IsOffline: false, IsReadOnly: false);
        var snapshot = Snapshot(
            disk,
            new PartitionSnapshot(new StoragePartitionRecord(1, 1 * Mib, 8 * Gib, null, null, []), null, null),
            new PartitionSnapshot(new StoragePartitionRecord(2, 9 * Gib, 7 * Gib, null, null, []), null, null));

        Disk built = StorageInventoryMapper.BuildDisk(snapshot, isSystem: false, WindowsVolume, NoEncryption());

        built.Id.DiskGuid.ShouldBe(Guid.Empty);
        built.Id.Number.ShouldBe(4);
        built.PartitionStyle.ShouldBe(PartitionStyle.Mbr);
        built.SerialNumber.ShouldBe(string.Empty);
        built.Partitions.Count.ShouldBe(2);
        built.Partitions.All(p => p.Role == PartitionRole.Unsupported).ShouldBeTrue();
        built.Partitions.Select(p => p.UniqueId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public void GptDiskWithoutADiskGuidIsRefused()
    {
        var disk = GptDisk(0, DiskGuid) with { Guid = null };

        Should.Throw<ArgumentException>(() =>
            StorageInventoryMapper.BuildDisk(Snapshot(disk), isSystem: true, WindowsVolume, NoEncryption()));
    }

    [Fact]
    public void GptPartitionWithoutAPartitionGuidIsRefused()
    {
        var snapshot = Snapshot(
            GptDisk(0, DiskGuid),
            new PartitionSnapshot(new StoragePartitionRecord(1, 1 * Mib, 10 * Mib, Braced(BasicType), null, []), null, null));

        Should.Throw<ArgumentException>(() =>
            StorageInventoryMapper.BuildDisk(snapshot, isSystem: false, WindowsVolume, NoEncryption()));
    }

    [Fact]
    public void ZeroLengthPartitionIsRefused()
    {
        var snapshot = Snapshot(GptDisk(0, DiskGuid), PartSnap(1, 1 * Mib, 0, BasicType, 0x33));

        Should.Throw<ArgumentException>(() =>
            StorageInventoryMapper.BuildDisk(snapshot, isSystem: false, WindowsVolume, NoEncryption()));
    }

    [Fact]
    public void SectorSizeOtherThan512Or4096IsRefused()
    {
        var disk = GptDisk(0, DiskGuid) with { LogicalSectorSize = 2048 };

        Should.Throw<ArgumentException>(() =>
            StorageInventoryMapper.BuildDisk(Snapshot(disk), isSystem: true, WindowsVolume, NoEncryption()));
    }

    // ---- helpers ----

    private static Dictionary<string, EncryptionState> NoEncryption() => new(StringComparer.Ordinal);

    private static DiskSnapshot Snapshot(StorageDiskRecord disk, params PartitionSnapshot[] partitions) =>
        new(disk, partitions, []);

    private static Guid GuidOf(byte seed) => Guid.Parse($"00000000-0000-0000-0000-0000000000{seed:X2}");

    /// <summary>
    /// A partition snapshot. When <paramref name="volumePath"/> is given, the partition has that mount point and the
    /// given volume row, as the reader would assemble them.
    /// </summary>
    private static PartitionSnapshot PartSnap(
        uint number,
        ulong offset,
        ulong size,
        Guid type,
        byte guidSeed,
        string? volumePath = null,
        StorageVolumeRecord? volume = null)
    {
        StoragePartitionRecord row = PartitionRow(
            number,
            offset,
            size,
            type,
            GuidOf(guidSeed),
            volumePath is null ? [] : [volumePath]);

        return new PartitionSnapshot(row, volumePath, volume);
    }
}
