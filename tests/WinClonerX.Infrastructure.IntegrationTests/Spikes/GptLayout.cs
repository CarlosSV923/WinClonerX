using System.Buffers.Binary;
using System.Text;

namespace WinClonerX.Infrastructure.IntegrationTests.Spikes;

/// <summary>
/// Pure helpers for the hand-made GPT of spike S1: CRC32 (IEEE 802.3, reflected, polynomial 0xEDB88320) and the byte
/// layout of the protective MBR, the GPT headers and the partition entry array (UEFI specification, section 5.3).
/// Nothing in this class touches a device.
/// </summary>
internal static class GptLayout
{
    public const int SectorSize = 512;
    public const int HeaderSize = 92;
    public const int EntryCount = 128;
    public const int EntrySize = 128;
    public const int EntryArrayBytes = EntryCount * EntrySize;
    public const int EntryArraySectors = EntryArrayBytes / SectorSize;
    public const uint Revision = 0x00010000;
    public const long FirstUsableLba = 34;

    /// <summary>EBD0A0A2-B9E5-4433-87C0-68B6B72699C7, Basic Data partition.</summary>
    public static readonly Guid BasicDataType = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");

    private static readonly uint[] CrcTable = BuildCrcTable();

    /// <summary>CRC32 as used by GPT (IEEE 802.3, reflected, initial value and final XOR 0xFFFFFFFF).</summary>
    public static uint Crc32(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFFu;
        foreach (byte b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFFu] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    /// <summary>
    /// Protective MBR at LBA 0: one partition entry of type 0xEE that starts at LBA 1 and covers the disk, and the
    /// 0x55 0xAA boot signature (bytes 510 and 511, which read as 0xAA55 little-endian).
    /// </summary>
    public static byte[] BuildProtectiveMbr(long lastLba)
    {
        const int entry = 446;
        byte[] sector = new byte[SectorSize];

        sector[entry + 0] = 0x00; // status: not active
        sector[entry + 1] = 0x00; // CHS first: head 0
        sector[entry + 2] = 0x02; // CHS first: sector 2 (LBA 1)
        sector[entry + 3] = 0x00; // CHS first: cylinder 0
        sector[entry + 4] = 0xEE; // type: GPT protective
        sector[entry + 5] = 0xFF; // CHS last: maximum, as used for disks above the CHS limit
        sector[entry + 6] = 0xFF;
        sector[entry + 7] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(entry + 8, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(
            sector.AsSpan(entry + 12, 4), (uint)Math.Min(lastLba, (long)uint.MaxValue));

        sector[510] = 0x55;
        sector[511] = 0xAA;
        return sector;
    }

    /// <summary>
    /// The 128-entry partition array (16384 bytes). Only entry 0 is used; the others stay zero.
    /// </summary>
    public static byte[] BuildEntryArray(
        Guid partitionType,
        Guid uniquePartitionId,
        long firstLba,
        long lastLba,
        string name)
    {
        byte[] array = new byte[EntryArrayBytes];
        Span<byte> entry = array.AsSpan(0, EntrySize);

        partitionType.ToByteArray().CopyTo(entry.Slice(0, 16));
        uniquePartitionId.ToByteArray().CopyTo(entry.Slice(16, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(32, 8), (ulong)firstLba);
        BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(40, 8), (ulong)lastLba);

        // Attributes (offset 48) stay zero. The name is UTF-16LE, at most 36 characters, zero padded.
        Encoding.Unicode.GetBytes(name).AsSpan().CopyTo(entry.Slice(56, 72));
        return array;
    }

    /// <summary>
    /// A 92-byte GPT header padded to one sector. The primary header sits at LBA 1 and points to the entries at LBA 2;
    /// the backup header sits at the last LBA and points to the entries at last LBA - 32.
    /// </summary>
    public static byte[] BuildHeader(bool primary, long lastLba, Guid diskGuid, uint entriesCrc)
    {
        byte[] sector = new byte[SectorSize];
        Span<byte> header = sector.AsSpan(0, HeaderSize);

        "EFI PART"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), Revision);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), HeaderSize);

        // HeaderCRC32 (offset 16) is zero while the CRC is computed. Reserved (offset 20) stays zero.
        long myLba = primary ? 1 : lastLba;
        long alternateLba = primary ? lastLba : 1;
        long entriesLba = primary ? 2 : lastLba - EntryArraySectors;

        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(24, 8), (ulong)myLba);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(32, 8), (ulong)alternateLba);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(40, 8), (ulong)FirstUsableLba);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(48, 8), (ulong)(lastLba - 33));
        diskGuid.ToByteArray().CopyTo(header.Slice(56, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(72, 8), (ulong)entriesLba);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(80, 4), EntryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(84, 4), EntrySize);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(88, 4), entriesCrc);

        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(16, 4), Crc32(header));
        return sector;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint n = 0; n < table.Length; n++)
        {
            uint c = n;
            for (int k = 0; k < 8; k++)
            {
                c = (c & 1u) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
