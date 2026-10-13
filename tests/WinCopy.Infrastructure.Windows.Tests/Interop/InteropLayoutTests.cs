using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Shouldly;
using WinCopy.Infrastructure.Windows.Interop;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Interop;

// These tests only inspect struct layouts, constants and the SafeHandle type hierarchy.
// They never open a device or disk: no CreateFile, no DeviceIoControl.
public sealed class InteropLayoutTests
{
    private const uint Buffered = 0;
    private const uint NeitherMethod = 3;
    private const uint AccessAny = 0;
    private const uint AccessRead = 1;
    private const uint AccessReadWrite = 3;

    private const uint DeviceDisk = 7;
    private const uint DeviceStorage = 0x2D;
    private const uint DeviceVolume = 0x56;
    private const uint DeviceFileSystem = 9;

    // ---- SafeDeviceHandle ----

    [Fact]
    public void SafeDeviceHandleDerivesFromZeroOrMinusOneIsInvalid()
    {
        typeof(SafeDeviceHandle).BaseType.ShouldBe(typeof(SafeHandleZeroOrMinusOneIsInvalid));
    }

    // ---- Struct sizes (x64) ----

    [Fact]
    public void StoragePropertyQueryIs12Bytes() => Unsafe.SizeOf<STORAGE_PROPERTY_QUERY>().ShouldBe(12);

    [Fact]
    public void StorageDeviceDescriptorIs40BytesIncludingOneByteArray() =>
        Unsafe.SizeOf<STORAGE_DEVICE_DESCRIPTOR>().ShouldBe(40);

    [Fact]
    public void StorageDeviceDescriptorRawPropertiesArrayStartsAfter36HeaderBytes() =>
        Marshal.OffsetOf<STORAGE_DEVICE_DESCRIPTOR>(nameof(STORAGE_DEVICE_DESCRIPTOR.RawDeviceProperties))
            .ToInt32().ShouldBe(36);

    [Fact]
    public void StorageDeviceNumberIs12Bytes() => Unsafe.SizeOf<STORAGE_DEVICE_NUMBER>().ShouldBe(12);

    [Fact]
    public void DiskExtentIs24Bytes() => Unsafe.SizeOf<DISK_EXTENT>().ShouldBe(24);

    [Fact]
    public void DiskExtentStartingOffsetIsAt8() =>
        Marshal.OffsetOf<DISK_EXTENT>(nameof(DISK_EXTENT.StartingOffset)).ToInt32().ShouldBe(8);

    [Fact]
    public void PartitionInformationGptIs112Bytes() => Unsafe.SizeOf<PARTITION_INFORMATION_GPT>().ShouldBe(112);

    [Fact]
    public void PartitionInformationExIs144Bytes() => Unsafe.SizeOf<PARTITION_INFORMATION_EX>().ShouldBe(144);

    [Fact]
    public void PartitionInformationExGptAndMbrShareStorageAt32()
    {
        Marshal.OffsetOf<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.Gpt)).ToInt32().ShouldBe(32);
        Marshal.OffsetOf<PARTITION_INFORMATION_EX>(nameof(PARTITION_INFORMATION_EX.Mbr)).ToInt32().ShouldBe(32);
    }

    [Fact]
    public void PartitionInformationExSizeConstantMatchesStruct() =>
        IoControlConstants.PARTITION_INFORMATION_EX_SIZE.ShouldBe(Unsafe.SizeOf<PARTITION_INFORMATION_EX>());

    [Fact]
    public void DriveLayoutInformationGptIs40Bytes() => Unsafe.SizeOf<DRIVE_LAYOUT_INFORMATION_GPT>().ShouldBe(40);

    [Fact]
    public void DriveLayoutInformationExHeaderIs48Bytes() =>
        Unsafe.SizeOf<DRIVE_LAYOUT_INFORMATION_EX>().ShouldBe(48);

    [Fact]
    public void CreateDiskGptIs20Bytes() => Unsafe.SizeOf<CREATE_DISK_GPT>().ShouldBe(20);

    [Fact]
    public void CreateDiskIs24Bytes() => Unsafe.SizeOf<CREATE_DISK>().ShouldBe(24);

    [Fact]
    public void GetDiskAttributesIs16Bytes() => Unsafe.SizeOf<GET_DISK_ATTRIBUTES>().ShouldBe(16);

    [Fact]
    public void SetDiskAttributesIs40Bytes() => Unsafe.SizeOf<SET_DISK_ATTRIBUTES>().ShouldBe(40);

    [Fact]
    public void SetDiskAttributesAttributesIsAt8() =>
        Marshal.OffsetOf<SET_DISK_ATTRIBUTES>(nameof(SET_DISK_ATTRIBUTES.Attributes)).ToInt32().ShouldBe(8);

    [Fact]
    public void GetLengthInformationIs8Bytes() => Unsafe.SizeOf<GET_LENGTH_INFORMATION>().ShouldBe(8);

    [Fact]
    public void NtfsVolumeDataBufferIs96Bytes() => Unsafe.SizeOf<NTFS_VOLUME_DATA_BUFFER>().ShouldBe(96);

    [Fact]
    public void NtfsVolumeDataBufferMftValidDataLengthIsAt56() =>
        Marshal.OffsetOf<NTFS_VOLUME_DATA_BUFFER>(nameof(NTFS_VOLUME_DATA_BUFFER.MftValidDataLength))
            .ToInt32().ShouldBe(56);

    [Fact]
    public void StartingLcnInputBufferIs8Bytes() => Unsafe.SizeOf<STARTING_LCN_INPUT_BUFFER>().ShouldBe(8);

    [Fact]
    public void VolumeBitmapBufferHeaderIs16Bytes() => Unsafe.SizeOf<VOLUME_BITMAP_BUFFER>().ShouldBe(16);

    [Fact]
    public void SystemPowerStatusIs12Bytes() => Unsafe.SizeOf<SYSTEM_POWER_STATUS>().ShouldBe(12);

    // ---- Enum and constant values ----

    [Theory]
    [InlineData(nameof(STORAGE_BUS_TYPE.BusTypeUsb), 7)]
    [InlineData(nameof(STORAGE_BUS_TYPE.BusTypeSata), 11)]
    [InlineData(nameof(STORAGE_BUS_TYPE.BusTypeSas), 10)]
    [InlineData(nameof(STORAGE_BUS_TYPE.BusTypeRAID), 8)]
    [InlineData(nameof(STORAGE_BUS_TYPE.BusTypeNvme), 17)]
    [InlineData(nameof(STORAGE_BUS_TYPE.BusTypeSpaces), 16)]
    [InlineData(nameof(STORAGE_BUS_TYPE.BusTypeFileBackedVirtual), 15)]
    public void StorageBusTypeHasSdkValues(string name, int expected) =>
        ((int)Enum.Parse<STORAGE_BUS_TYPE>(name)).ShouldBe(expected);

    [Fact]
    public void DiskAttributeConstantsHaveSdkValues()
    {
        IoControlConstants.DISK_ATTRIBUTE_OFFLINE.ShouldBe(0x1UL);
        IoControlConstants.DISK_ATTRIBUTE_READ_ONLY.ShouldBe(0x2UL);
    }

    [Fact]
    public void Kernel32ConstantsHaveSdkValues()
    {
        Kernel32.GENERIC_READ.ShouldBe(0x80000000u);
        Kernel32.GENERIC_WRITE.ShouldBe(0x40000000u);
        Kernel32.FILE_SHARE_READ.ShouldBe(1u);
        Kernel32.FILE_SHARE_WRITE.ShouldBe(2u);
        Kernel32.OPEN_EXISTING.ShouldBe(3u);
        Kernel32.FILE_FLAG_NO_BUFFERING.ShouldBe(0x20000000u);
        Kernel32.FILE_FLAG_WRITE_THROUGH.ShouldBe(0x80000000u);
    }

    [Fact]
    public void FirmwareTypeAndExecutionStateValuesAreSdkValues()
    {
        ((int)FIRMWARE_TYPE.Unknown).ShouldBe(0);
        ((int)FIRMWARE_TYPE.Bios).ShouldBe(1);
        ((int)FIRMWARE_TYPE.Uefi).ShouldBe(2);
        ((int)FIRMWARE_TYPE.Max).ShouldBe(3);
        ((uint)EXECUTION_STATE.ES_SYSTEM_REQUIRED).ShouldBe(0x1u);
        ((uint)EXECUTION_STATE.ES_CONTINUOUS).ShouldBe(0x80000000u);
    }

    // ---- IOCTL and FSCTL codes ----

    [Fact]
    public void CtlCodeComposesFieldsLikeWin32Macro()
    {
        // CTL_CODE(DeviceType, Function, Method, Access)
        IoControlCodes.CtlCode(DeviceDisk, 0x14, Buffered, AccessAny).ShouldBe(0x00070050u);
        IoControlCodes.CtlCode(DeviceFileSystem, 0x1B, NeitherMethod, AccessAny).ShouldBe(0x0009006Fu);
    }

    // Each constant is read by name (so the production value is tested, not a copy)
    // and compared with the value computed from its components.
    [Theory]
    // Disk
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_GET_DRIVE_LAYOUT_EX), DeviceDisk, 0x14u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_SET_DRIVE_LAYOUT_EX), DeviceDisk, 0x15u, Buffered, AccessReadWrite)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_CREATE_DISK), DeviceDisk, 0x16u, Buffered, AccessReadWrite)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_DELETE_DRIVE_LAYOUT), DeviceDisk, 0x40u, Buffered, AccessReadWrite)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_UPDATE_PROPERTIES), DeviceDisk, 0x50u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX), DeviceDisk, 0x28u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_GET_LENGTH_INFO), DeviceDisk, 0x17u, Buffered, AccessRead)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_GET_DISK_ATTRIBUTES), DeviceDisk, 0x3Cu, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.IOCTL_DISK_SET_DISK_ATTRIBUTES), DeviceDisk, 0x3Du, Buffered, AccessReadWrite)]
    // Storage
    [InlineData(nameof(IoControlCodes.IOCTL_STORAGE_QUERY_PROPERTY), DeviceStorage, 0x500u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.IOCTL_STORAGE_GET_DEVICE_NUMBER), DeviceStorage, 0x420u, Buffered, AccessAny)]
    // Volume
    [InlineData(nameof(IoControlCodes.IOCTL_VOLUME_GET_VOLUME_DISK_EXTENTS), DeviceVolume, 0u, Buffered, AccessAny)]
    // File system control
    [InlineData(nameof(IoControlCodes.FSCTL_LOCK_VOLUME), DeviceFileSystem, 6u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.FSCTL_UNLOCK_VOLUME), DeviceFileSystem, 7u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.FSCTL_DISMOUNT_VOLUME), DeviceFileSystem, 8u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.FSCTL_GET_NTFS_VOLUME_DATA), DeviceFileSystem, 25u, Buffered, AccessAny)]
    [InlineData(nameof(IoControlCodes.FSCTL_GET_VOLUME_BITMAP), DeviceFileSystem, 27u, NeitherMethod, AccessAny)]
    [InlineData(nameof(IoControlCodes.FSCTL_EXTEND_VOLUME), DeviceFileSystem, 60u, Buffered, AccessAny)]
    public void IoControlConstantEqualsCtlCodeOfItsComponents(
        string constantName, uint deviceType, uint function, uint method, uint access)
    {
        FieldInfo field = typeof(IoControlCodes).GetField(constantName, BindingFlags.Public | BindingFlags.Static)
            ?? throw new InvalidOperationException($"Constant {constantName} not found.");

        var actual = (uint)field.GetRawConstantValue()!;

        actual.ShouldBe(IoControlCodes.CtlCode(deviceType, function, method, access), customMessage: constantName);
    }
}
