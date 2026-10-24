using Shouldly;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Shared;
using Xunit;

namespace WinClonerX.Domain.Tests.Inventory;

public sealed class DiskInventoryTests
{
    private const long Mib = 1024L * 1024;
    private const long Gib = 1024L * 1024 * 1024;

    private static readonly DateTimeOffset CapturedAt = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    private static readonly int[] ExpectedPartitionOrder = [2, 3, 1];
    private static readonly DiskId SourceId = new(0, Id(100));
    private static readonly DiskId TargetId = new(1, Id(200));

    [Fact]
    public void RoleOfAnEFIPartitionIsEfiSystem()
    {
        RoleOf(GptPartitionType.EfiSystem).ShouldBe(PartitionRole.EfiSystem);
    }

    [Fact]
    public void RoleOfAMicrosoftReservedPartitionIsMsReserved()
    {
        RoleOf(GptPartitionType.MicrosoftReserved).ShouldBe(PartitionRole.MsReserved);
    }

    [Fact]
    public void RoleOfAWindowsRecoveryPartitionIsRecovery()
    {
        RoleOf(GptPartitionType.WindowsRecovery).ShouldBe(PartitionRole.Recovery);
    }

    [Fact]
    public void RoleOfABasicDataPartitionWithTheWindowsVolumeIsWindows()
    {
        RoleOf(GptPartitionType.BasicData, WindowsVolume()).ShouldBe(PartitionRole.Windows);
    }

    [Fact]
    public void RoleOfABasicDataPartitionWithAnotherVolumeIsData()
    {
        RoleOf(GptPartitionType.BasicData, OtherVolume()).ShouldBe(PartitionRole.Data);
    }

    [Fact]
    public void RoleOfABasicDataPartitionWithoutAVolumeIsData()
    {
        RoleOf(GptPartitionType.BasicData, null).ShouldBe(PartitionRole.Data);
    }

    [Fact]
    public void RoleOfAnLDMMetadataPartitionIsUnsupported()
    {
        RoleOf(GptPartitionType.LdmMetadata).ShouldBe(PartitionRole.Unsupported);
    }

    [Fact]
    public void RoleOfAnLDMDataPartitionIsUnsupported()
    {
        RoleOf(GptPartitionType.LdmData).ShouldBe(PartitionRole.Unsupported);
    }

    [Fact]
    public void RoleOfAStorageSpacesPartitionIsUnsupported()
    {
        RoleOf(GptPartitionType.StorageSpaces).ShouldBe(PartitionRole.Unsupported);
    }

    [Fact]
    public void RoleOfAnUnknownTypeIsUnsupported()
    {
        RoleOf(new GptPartitionType(Id(999))).ShouldBe(PartitionRole.Unsupported);
    }

    [Fact]
    public void RoleOfAnEFIPartitionIsNotChangedByAWindowsVolume()
    {
        RoleOf(GptPartitionType.EfiSystem, WindowsVolume()).ShouldBe(PartitionRole.EfiSystem);
    }

    [Fact]
    public void VolumeRejectsUsedSpaceGreaterThanCapacity()
    {
        Should.Throw<ArgumentException>(() => new Volume(
            FileSystemKind.Ntfs, new ByteSize(10), new ByteSize(11), null, EncryptionState.None, false, null));
    }

    [Fact]
    public void VolumeAcceptsUsedSpaceEqualToCapacity()
    {
        var volume = new Volume(
            FileSystemKind.Ntfs, new ByteSize(10), new ByteSize(10), null, EncryptionState.None, false, null);

        volume.Used.ShouldBe(volume.Capacity);
    }

    [Fact]
    public void DiskOrdersPartitionsByOffset()
    {
        var disk = CreateDisk(SourceId, true,
            Part(1, 2 * Gib, Gib, GptPartitionType.BasicData),
            Part(2, 0, Mib, GptPartitionType.EfiSystem),
            Part(3, Gib, Gib, GptPartitionType.MicrosoftReserved));

        disk.Partitions.Select(p => p.Number).ToArray().ShouldBe(ExpectedPartitionOrder);
    }

    [Fact]
    public void InventoryWithoutASystemDiskIsNotSupported()
    {
        var inventory = CreateInventory(CreateDisk(SourceId, false), CreateDisk(TargetId, false));

        inventory.IsSupported.ShouldBeFalse();
        inventory.UnsupportedReason.ShouldBe("NoSystemDisk");
        inventory.SystemDisk.ShouldBeNull();
    }

    [Fact]
    public void InventoryWithoutAnyDiskIsNotSupported()
    {
        var inventory = CreateInventory();

        inventory.IsSupported.ShouldBeFalse();
        inventory.UnsupportedReason.ShouldBe("NoSystemDisk");
    }

    [Fact]
    public void InventoryWithExactlyOneSystemDiskIsSupported()
    {
        var system = CreateDisk(SourceId, true);
        var inventory = CreateInventory(system, CreateDisk(TargetId, false));

        inventory.IsSupported.ShouldBeTrue();
        inventory.UnsupportedReason.ShouldBeNull();
        inventory.SystemDisk.ShouldBe(system);
    }

    [Fact]
    public void InventoryWithTwoSystemDisksIsNotSupported()
    {
        var inventory = CreateInventory(CreateDisk(SourceId, true), CreateDisk(TargetId, true));

        inventory.IsSupported.ShouldBeFalse();
        inventory.UnsupportedReason.ShouldBe("MultipleSystemDisks");
        inventory.SystemDisk.ShouldBeNull();
    }

    [Fact]
    public void FindDiskReturnsTheDiskWithTheSameGuidEvenIfTheNumberDiffers()
    {
        var target = CreateDisk(TargetId, false);
        var inventory = CreateInventory(CreateDisk(SourceId, true), target);

        inventory.FindDisk(new DiskId(42, TargetId.DiskGuid)).ShouldBe(target);
    }

    [Fact]
    public void FindDiskReturnsNullForAnUnknownDisk()
    {
        var inventory = CreateInventory(CreateDisk(SourceId, true));

        inventory.FindDisk(new DiskId(9, Id(999))).ShouldBeNull();
    }

    [Fact]
    public void FingerprintIsStableForTheSameInventory()
    {
        var inventory = Baseline();

        Fingerprint(inventory).ShouldBe(Fingerprint(inventory));
        Fingerprint(Baseline()).ShouldBe(Fingerprint(inventory));
    }

    [Fact]
    public void FingerprintIsIndependentOfTheCaptureTime()
    {
        Fingerprint(MakeInventory(SourceDisk(), TargetDisk(), CapturedAt.AddDays(3)))
            .ShouldBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintIsIndependentOfTheModel()
    {
        var source = SourceDisk() with { Model = "Otro modelo" };

        Fingerprint(MakeInventory(source, TargetDisk())).ShouldBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenTheSourceGuidChanges()
    {
        var source = SourceDisk() with { Id = new DiskId(0, Id(101)) };
        var inventory = MakeInventory(source, TargetDisk());

        inventory.Fingerprint(source.Id, TargetId).ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenTheSourceSerialChanges()
    {
        Fingerprint(MakeInventory(SourceDisk() with { SerialNumber = "SRC-2" }, TargetDisk()))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenTheSourceSizeChanges()
    {
        Fingerprint(MakeInventory(SourceDisk() with { Size = ByteSize.FromGiB(512) }, TargetDisk()))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenASourcePartitionNumberChanges()
    {
        Fingerprint(WithSourceWindowsPartition(p => p with { Number = 9 }))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenASourcePartitionOffsetChanges()
    {
        Fingerprint(WithSourceWindowsPartition(p => p with { Extent = new Extent(p.Extent.Offset + Mib, p.Extent.Length) }))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenASourcePartitionLengthChanges()
    {
        Fingerprint(WithSourceWindowsPartition(p => p with { Extent = new Extent(p.Extent.Offset, p.Extent.Length + Mib) }))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenASourcePartitionTypeChanges()
    {
        Fingerprint(WithSourceWindowsPartition(p => p with { Type = GptPartitionType.LdmData }))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenASourcePartitionUniqueIdChanges()
    {
        Fingerprint(WithSourceWindowsPartition(p => p with { UniqueId = new PartitionGuid(Id(999)) }))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintChangesWhenTheTargetSerialChanges()
    {
        Fingerprint(MakeInventory(SourceDisk(), TargetDisk() with { SerialNumber = "TGT-2" }))
            .ShouldNotBe(Fingerprint(Baseline()));
    }

    [Fact]
    public void FingerprintDependsOnTheOrderOfSourceAndTarget()
    {
        var inventory = Baseline();

        inventory.Fingerprint(SourceId, TargetId).ShouldNotBe(inventory.Fingerprint(TargetId, SourceId));
    }

    [Fact]
    public void FingerprintForAnUnknownDiskIsRejected()
    {
        Should.Throw<ArgumentException>(() => Baseline().Fingerprint(new DiskId(9, Id(999)), TargetId));
    }

    private static Guid Id(int n) => new($"00000000-0000-0000-0000-{n:D12}");

    private static PartitionRole RoleOf(GptPartitionType type, Volume? volume = null) =>
        Part(1, Mib, 100 * Mib, type, volume).Role;

    private static Partition Part(int number, long offset, long length, GptPartitionType type, Volume? volume = null) =>
        new(number, new Extent(offset, length), type, new PartitionGuid(Id(number)), 0UL, "Partición", volume);

    private static Volume WindowsVolume() =>
        new(FileSystemKind.Ntfs, ByteSize.FromGiB(100), ByteSize.FromGiB(40), new ByteSize(4096), EncryptionState.None, true, null);

    private static Volume OtherVolume() =>
        new(FileSystemKind.Ntfs, ByteSize.FromGiB(50), ByteSize.FromGiB(10), null, EncryptionState.None, false, null);

    private static Disk CreateDisk(DiskId id, bool isSystem, params Partition[] partitions) =>
        new(id, "Modelo de prueba", "SN-1234", ByteSize.FromGiB(256), SectorSize.Bytes512, SectorSize.Bytes4096,
            BusKind.Nvme, PartitionStyle.Gpt, isSystem, partitions, IsOffline: false, IsReadOnly: false);

    private static DiskInventory CreateInventory(params Disk[] disks) =>
        new(CapturedAt, FirmwareType.Uefi, disks);

    private static DiskInventory MakeInventory(Disk source, Disk target, DateTimeOffset? capturedAt = null) =>
        new(capturedAt ?? CapturedAt, FirmwareType.Uefi, new[] { source, target });

    private static DiskInventory Baseline() => MakeInventory(SourceDisk(), TargetDisk());

    private static InventoryFingerprint Fingerprint(DiskInventory inventory) => inventory.Fingerprint(SourceId, TargetId);

    private static Partition EfiPartition() =>
        new(1, new Extent(Mib, 100 * Mib), GptPartitionType.EfiSystem, new PartitionGuid(Id(1)), 0UL, "EFI", null);

    private static Partition MsrPartition() =>
        new(2, new Extent(101 * Mib, 16 * Mib), GptPartitionType.MicrosoftReserved, new PartitionGuid(Id(2)), 0UL, "MSR", null);

    private static Partition WindowsPartition() =>
        new(3, new Extent(117 * Mib, 200 * Gib), GptPartitionType.BasicData, new PartitionGuid(Id(3)), 0UL, "Windows", WindowsVolume());

    private static Disk SourceDisk() =>
        new(SourceId, "Modelo origen", "SRC-1", ByteSize.FromGiB(256), SectorSize.Bytes512, SectorSize.Bytes4096,
            BusKind.Nvme, PartitionStyle.Gpt, true, new[] { EfiPartition(), MsrPartition(), WindowsPartition() },
            IsOffline: false, IsReadOnly: false);

    private static Disk TargetDisk() =>
        new(TargetId, "Modelo destino", "TGT-1", ByteSize.FromGiB(512), SectorSize.Bytes512, SectorSize.Bytes4096,
            BusKind.Usb, PartitionStyle.Gpt, false, Array.Empty<Partition>(), IsOffline: false, IsReadOnly: false);

    private static DiskInventory WithSourceWindowsPartition(Func<Partition, Partition> change)
    {
        var source = SourceDisk();
        var partitions = source.Partitions.ToArray();
        partitions[^1] = change(partitions[^1]);

        var changedSource = new Disk(source.Id, source.Model, source.SerialNumber, source.Size, source.LogicalSectorSize,
            source.PhysicalSectorSize, source.Bus, source.PartitionStyle, source.IsSystem, partitions,
            source.IsOffline, source.IsReadOnly);

        return MakeInventory(changedSource, TargetDisk());
    }
}
