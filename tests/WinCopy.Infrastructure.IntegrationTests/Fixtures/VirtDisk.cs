using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace WinCopy.Infrastructure.IntegrationTests.Fixtures;

/// <summary>
/// P/Invoke declarations for the VHDX part of <c>virtdisk.dll</c>. Every function returns a Win32 error code,
/// where 0 means success. Only <see cref="VirtualDiskFixture"/> should call these.
/// </summary>
internal static partial class VirtDisk
{
    /// <summary>VIRTUAL_STORAGE_TYPE.DeviceId for VHDX files.</summary>
    internal const uint VHDX_DEVICE_ID = 3;

    /// <summary>VIRTUAL_STORAGE_TYPE.VendorId for Microsoft virtual disks.</summary>
    internal static readonly Guid MicrosoftVendorId = new("EC984AEC-A0F9-47E9-901F-71415A66345B");

    internal const uint VIRTUAL_DISK_ACCESS_NONE = 0;
    internal const uint CREATE_VIRTUAL_DISK_FLAG_NONE = 0;
    internal const int CREATE_VIRTUAL_DISK_VERSION_2 = 2;
    internal const int ATTACH_VIRTUAL_DISK_VERSION_1 = 1;

    /// <summary>No drive letter is assigned. PERMANENT_LIFETIME (0x4) is deliberately not used, so the disk detaches when the process dies.</summary>
    internal const uint ATTACH_VIRTUAL_DISK_FLAG_NO_DRIVE_LETTER = 0x00000002;

    internal static VIRTUAL_STORAGE_TYPE VhdxStorageType() =>
        new() { DeviceId = VHDX_DEVICE_ID, VendorId = MicrosoftVendorId };

    [LibraryImport("virtdisk.dll", StringMarshalling = StringMarshalling.Utf16)]
    internal static partial uint CreateVirtualDisk(
        ref VIRTUAL_STORAGE_TYPE virtualStorageType,
        string path,
        uint virtualDiskAccessMask,
        IntPtr securityDescriptor,
        uint flags,
        uint providerSpecificFlags,
        ref CREATE_VIRTUAL_DISK_PARAMETERS parameters,
        IntPtr overlapped,
        out SafeFileHandle handle);

    [LibraryImport("virtdisk.dll")]
    internal static partial uint AttachVirtualDisk(
        SafeFileHandle handle,
        IntPtr securityDescriptor,
        uint flags,
        uint providerSpecificFlags,
        ref ATTACH_VIRTUAL_DISK_PARAMETERS parameters,
        IntPtr overlapped);

    /// <summary>Writes the physical path, for example <c>\\.\PhysicalDrive5</c>, as a NUL-terminated UTF-16 string.</summary>
    [LibraryImport("virtdisk.dll")]
    internal static unsafe partial uint GetVirtualDiskPhysicalPath(
        SafeFileHandle handle,
        ref uint diskPathSizeInBytes,
        char* diskPath);

    [LibraryImport("virtdisk.dll")]
    internal static partial uint DetachVirtualDisk(
        SafeFileHandle handle,
        uint flags,
        uint providerSpecificFlags);
}

/// <summary>VIRTUAL_STORAGE_TYPE. 20 bytes on x64.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VIRTUAL_STORAGE_TYPE
{
    public uint DeviceId;
    public Guid VendorId;
}

/// <summary>CREATE_VIRTUAL_DISK_PARAMETERS, version 2 view. 128 bytes on x64.</summary>
[StructLayout(LayoutKind.Explicit, Size = 128)]
internal struct CREATE_VIRTUAL_DISK_PARAMETERS
{
    [FieldOffset(0)]
    public int Version;

    [FieldOffset(8)]
    public Guid UniqueId;

    [FieldOffset(24)]
    public ulong MaximumSize;

    [FieldOffset(32)]
    public uint BlockSizeInBytes;

    [FieldOffset(36)]
    public uint SectorSizeInBytes;

    [FieldOffset(40)]
    public uint PhysicalSectorSizeInBytes;

    [FieldOffset(48)]
    public IntPtr ParentPath;

    [FieldOffset(56)]
    public IntPtr SourcePath;

    [FieldOffset(64)]
    public int OpenFlags;

    [FieldOffset(68)]
    public VIRTUAL_STORAGE_TYPE ParentVirtualStorageType;

    [FieldOffset(88)]
    public VIRTUAL_STORAGE_TYPE SourceVirtualStorageType;

    [FieldOffset(108)]
    public Guid ResiliencyGuid;
}

/// <summary>ATTACH_VIRTUAL_DISK_PARAMETERS. 8 bytes.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ATTACH_VIRTUAL_DISK_PARAMETERS
{
    public int Version;
    public int Reserved;
}
