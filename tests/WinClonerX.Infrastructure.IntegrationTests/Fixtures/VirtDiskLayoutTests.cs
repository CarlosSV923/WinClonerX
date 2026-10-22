using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Shouldly;
using Xunit;

namespace WinClonerX.Infrastructure.IntegrationTests.Fixtures;

// Layout checks only. These tests do not call virtdisk.dll, so they run without elevation.
[Trait("Category", "Integration")]
public sealed class VirtDiskLayoutTests
{
    [Fact]
    public void VirtualStorageTypeMatchesWin32Layout()
    {
        Unsafe.SizeOf<VIRTUAL_STORAGE_TYPE>().ShouldBe(20);

        AssertOffsets<VIRTUAL_STORAGE_TYPE>(
            (nameof(VIRTUAL_STORAGE_TYPE.DeviceId), 0),
            (nameof(VIRTUAL_STORAGE_TYPE.VendorId), 4));
    }

    [Fact]
    public void CreateVirtualDiskParametersMatchWin32Layout()
    {
        Unsafe.SizeOf<CREATE_VIRTUAL_DISK_PARAMETERS>().ShouldBe(128);

        AssertOffsets<CREATE_VIRTUAL_DISK_PARAMETERS>(
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.Version), 0),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.UniqueId), 8),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.MaximumSize), 24),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.BlockSizeInBytes), 32),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.SectorSizeInBytes), 36),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.PhysicalSectorSizeInBytes), 40),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.ParentPath), 48),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.SourcePath), 56),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.OpenFlags), 64),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.ParentVirtualStorageType), 68),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.SourceVirtualStorageType), 88),
            (nameof(CREATE_VIRTUAL_DISK_PARAMETERS.ResiliencyGuid), 108));
    }

    [Fact]
    public void AttachVirtualDiskParametersMatchWin32Layout()
    {
        Unsafe.SizeOf<ATTACH_VIRTUAL_DISK_PARAMETERS>().ShouldBe(8);

        AssertOffsets<ATTACH_VIRTUAL_DISK_PARAMETERS>(
            (nameof(ATTACH_VIRTUAL_DISK_PARAMETERS.Version), 0),
            (nameof(ATTACH_VIRTUAL_DISK_PARAMETERS.Reserved), 4));
    }

    [Fact]
    public void WellKnownValuesMatchTheSdk()
    {
        VirtDisk.VHDX_DEVICE_ID.ShouldBe(3u);
        VirtDisk.MicrosoftVendorId.ShouldBe(new Guid("EC984AEC-A0F9-47E9-901F-71415A66345B"));
        VirtDisk.ATTACH_VIRTUAL_DISK_FLAG_NO_DRIVE_LETTER.ShouldBe(2u);
        VirtDisk.VhdxStorageType().DeviceId.ShouldBe(3u);
    }

    private static void AssertOffsets<T>(params (string Field, int Offset)[] expected)
    {
        foreach ((string field, int offset) in expected)
        {
            Marshal.OffsetOf<T>(field).ToInt32().ShouldBe(offset, $"offset of {field}");
        }
    }
}
