using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace WinCopy.Infrastructure.Windows.Interop;

// Blittable mirrors of the Win32 structures used with DeviceIoControl and related APIs.
// Layouts target x64. Tests assert the sizes; keep them in sync with the Windows SDK headers.

internal static class IoControlConstants
{
    public const ulong DISK_ATTRIBUTE_OFFLINE = 0x1;
    public const ulong DISK_ATTRIBUTE_READ_ONLY = 0x2;

    /// <summary>Size of one PARTITION_INFORMATION_EX entry that follows DRIVE_LAYOUT_INFORMATION_EX.</summary>
    public const int PARTITION_INFORMATION_EX_SIZE = 144;
}

[InlineArray(1)]
internal struct ByteArray1
{
    private byte _element0;
}

[InlineArray(3)]
internal struct ByteArray3
{
    private byte _element0;
}

[InlineArray(4)]
internal struct UIntArray4
{
    private uint _element0;
}

[InlineArray(36)]
internal struct CharArray36
{
    private char _element0;
}

internal enum STORAGE_BUS_TYPE : int
{
    BusTypeUnknown = 0,
    BusTypeScsi = 1,
    BusTypeAtapi = 2,
    BusTypeAta = 3,
    BusType1394 = 4,
    BusTypeSsa = 5,
    BusTypeFibre = 6,
    BusTypeUsb = 7,
    BusTypeRAID = 8,
    BusTypeiScsi = 9,
    BusTypeSas = 10,
    BusTypeSata = 11,
    BusTypeSd = 12,
    BusTypeMmc = 13,
    BusTypeVirtual = 14,
    BusTypeFileBackedVirtual = 15,
    BusTypeSpaces = 16,
    BusTypeNvme = 17,
    BusTypeSCM = 18,
    BusTypeUfs = 19,
}

[StructLayout(LayoutKind.Sequential)]
internal struct STORAGE_PROPERTY_QUERY
{
    public int PropertyId;
    public int QueryType;
    public ByteArray1 AdditionalParameters;
}

/// <summary>
/// Header of STORAGE_DEVICE_DESCRIPTOR. The header fields occupy 36 bytes; with the trailing
/// one-byte RawDeviceProperties array the struct is 40 bytes.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct STORAGE_DEVICE_DESCRIPTOR
{
    public uint Version;
    public uint Size;
    public byte DeviceType;
    public byte DeviceTypeModifier;
    public byte RemovableMedia;
    public byte CommandQueueing;
    public uint VendorIdOffset;
    public uint ProductIdOffset;
    public uint ProductRevisionOffset;
    public uint SerialNumberOffset;
    public STORAGE_BUS_TYPE BusType;
    public uint RawPropertiesLength;
    public byte RawDeviceProperties;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STORAGE_DEVICE_NUMBER
{
    public uint DeviceType;
    public uint DeviceNumber;
    public uint PartitionNumber;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DISK_EXTENT
{
    public uint DiskNumber;
    public long StartingOffset;
    public long ExtentLength;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PARTITION_INFORMATION_GPT
{
    public Guid PartitionType;
    public Guid PartitionId;
    public ulong Attributes;
    public CharArray36 Name;
}

[StructLayout(LayoutKind.Sequential)]
internal struct PARTITION_INFORMATION_MBR
{
    public byte PartitionType;
    public byte BootIndicator;
    public byte RecognizedPartition;
    public uint HiddenSectors;
    public Guid PartitionId;
}

/// <summary>PARTITION_INFORMATION_EX. The MBR and GPT members share the same union storage at offset 32.</summary>
[StructLayout(LayoutKind.Explicit, Size = IoControlConstants.PARTITION_INFORMATION_EX_SIZE)]
internal struct PARTITION_INFORMATION_EX
{
    [FieldOffset(0)]
    public int PartitionStyle;

    [FieldOffset(8)]
    public long StartingOffset;

    [FieldOffset(16)]
    public long PartitionLength;

    [FieldOffset(24)]
    public uint PartitionNumber;

    [FieldOffset(28)]
    public byte RewritePartition;

    [FieldOffset(29)]
    public byte IsServicePartition;

    [FieldOffset(32)]
    public PARTITION_INFORMATION_GPT Gpt;

    [FieldOffset(32)]
    public PARTITION_INFORMATION_MBR Mbr;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DRIVE_LAYOUT_INFORMATION_GPT
{
    public Guid DiskId;
    public long StartingUsableOffset;
    public long UsableLength;
    public uint MaxPartitionCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct DRIVE_LAYOUT_INFORMATION_MBR
{
    public uint Signature;
    public uint CheckSum;
}

/// <summary>
/// Header of DRIVE_LAYOUT_INFORMATION_EX (48 bytes). It is followed in memory by PartitionCount
/// PARTITION_INFORMATION_EX entries, each <see cref="IoControlConstants.PARTITION_INFORMATION_EX_SIZE"/> bytes.
/// </summary>
[StructLayout(LayoutKind.Explicit, Size = 48)]
internal struct DRIVE_LAYOUT_INFORMATION_EX
{
    [FieldOffset(0)]
    public uint PartitionStyle;

    [FieldOffset(4)]
    public uint PartitionCount;

    [FieldOffset(8)]
    public DRIVE_LAYOUT_INFORMATION_MBR Mbr;

    [FieldOffset(8)]
    public DRIVE_LAYOUT_INFORMATION_GPT Gpt;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CREATE_DISK_GPT
{
    public Guid DiskId;
    public uint MaxPartitionCount;
}

[StructLayout(LayoutKind.Sequential)]
internal struct CREATE_DISK_MBR
{
    public uint Signature;
}

[StructLayout(LayoutKind.Explicit, Size = 24)]
internal struct CREATE_DISK
{
    [FieldOffset(0)]
    public uint PartitionStyle;

    [FieldOffset(4)]
    public CREATE_DISK_MBR Mbr;

    [FieldOffset(4)]
    public CREATE_DISK_GPT Gpt;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GET_DISK_ATTRIBUTES
{
    public uint Version;
    public uint Reserved1;
    public ulong Attributes;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SET_DISK_ATTRIBUTES
{
    public uint Version;
    public byte Persist;
    public ByteArray3 Reserved1;
    public ulong Attributes;
    public ulong AttributesMask;
    public UIntArray4 Reserved2;
}

[StructLayout(LayoutKind.Sequential)]
internal struct GET_LENGTH_INFORMATION
{
    public long Length;
}

[StructLayout(LayoutKind.Sequential)]
internal struct NTFS_VOLUME_DATA_BUFFER
{
    public long VolumeSerialNumber;
    public long NumberSectors;
    public long TotalClusters;
    public long FreeClusters;
    public long TotalReserved;
    public uint BytesPerSector;
    public uint BytesPerCluster;
    public uint BytesPerFileRecordSegment;
    public uint ClustersPerFileRecordSegment;
    public long MftValidDataLength;
    public long MftStartLcn;
    public long Mft2StartLcn;
    public long MftZoneStart;
    public long MftZoneEnd;
}

[StructLayout(LayoutKind.Sequential)]
internal struct STARTING_LCN_INPUT_BUFFER
{
    public long StartingLcn;
}

/// <summary>Header of VOLUME_BITMAP_BUFFER. The bitmap bytes follow it in memory.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct VOLUME_BITMAP_BUFFER
{
    public long StartingLcn;
    public long BitmapSize;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SYSTEM_POWER_STATUS
{
    public byte ACLineStatus;
    public byte BatteryFlag;
    public byte BatteryLifePercent;
    public byte SystemStatusFlag;
    public uint BatteryLifeTime;
    public uint BatteryFullLifeTime;
}
