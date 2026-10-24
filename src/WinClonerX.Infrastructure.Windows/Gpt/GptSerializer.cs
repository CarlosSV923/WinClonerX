using System.Buffers.Binary;
using System.Text;

namespace WinClonerX.Infrastructure.Windows.Gpt;

/// <summary>
/// Pure serializer for the byte layout of a GPT disk (UEFI specification, section 5.3): protective MBR, primary and
/// backup headers, and primary and backup partition entry arrays. It performs no I/O; the caller writes the result.
/// </summary>
internal static class GptSerializer
{
    public const int EntryCount = 128;
    public const int EntrySizeBytes = 128;
    public const int EntryArrayBytes = EntryCount * EntrySizeBytes;
    public const int HeaderSizeBytes = 92;
    public const int MaxNameChars = 36;

    private const uint Revision = 0x00010000;
    private const int MbrPartitionEntryOffset = 446;
    private const int NameOffset = 56;
    private const int NameSizeBytes = 72;
    private const int TypeOffset = 0;
    private const int UniqueIdOffset = 16;
    private const int FirstLbaOffset = 32;
    private const int LastLbaOffset = 40;
    private const int AttributesOffset = 48;

    /// <summary>Builds the five writes that describe the disk. Throws <see cref="ArgumentException"/> when the layout is invalid.</summary>
    public static GptImage Serialize(GptDiskLayout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(layout.Partitions);

        int sector = layout.LogicalSectorSize;
        if (sector != 512 && sector != 4096)
        {
            throw new ArgumentException($"The logical sector size must be 512 or 4096 bytes, not {sector}.", nameof(layout));
        }

        if (layout.DiskSizeBytes <= 0 || layout.DiskSizeBytes % sector != 0)
        {
            throw new ArgumentException(
                $"The disk size {layout.DiskSizeBytes} must be a positive multiple of the sector size {sector}.", nameof(layout));
        }

        long lastLba = layout.DiskSizeBytes / sector - 1;
        long entriesSectors = EntryArrayBytes / sector;
        long firstUsable = 2 + entriesSectors;
        long lastUsable = lastLba - 1 - entriesSectors;
        if (firstUsable > lastUsable)
        {
            throw new ArgumentException("The disk is too small to hold a GPT.", nameof(layout));
        }

        ValidatePartitions(layout.Partitions, firstUsable, lastUsable);

        byte[] entries = BuildEntryArray(layout.Partitions);
        uint entriesCrc = Crc32.Compute(entries);
        long backupEntriesLba = lastLba - entriesSectors;

        byte[] mbr = BuildProtectiveMbr(sector, lastLba);
        byte[] primaryHeader = BuildHeader(
            sector, myLba: 1, alternateLba: lastLba, firstUsable, lastUsable, layout.DiskGuid, entriesLba: 2, entriesCrc);
        byte[] backupHeader = BuildHeader(
            sector, myLba: lastLba, alternateLba: 1, firstUsable, lastUsable, layout.DiskGuid, backupEntriesLba, entriesCrc);

        return new GptImage(
        [
            new GptWrite(0, mbr),
            new GptWrite(sector, primaryHeader),
            new GptWrite(2L * sector, entries),
            new GptWrite(backupEntriesLba * sector, (byte[])entries.Clone()),
            new GptWrite(lastLba * sector, backupHeader),
        ]);
    }

    private static void ValidatePartitions(IReadOnlyList<GptPartitionEntry> partitions, long firstUsable, long lastUsable)
    {
        if (partitions.Count > EntryCount)
        {
            throw new ArgumentException(
                $"A GPT holds at most {EntryCount} partitions; {partitions.Count} were given.", nameof(partitions));
        }

        var uniqueIds = new HashSet<Guid>();
        for (int i = 0; i < partitions.Count; i++)
        {
            GptPartitionEntry p = partitions[i];
            ArgumentNullException.ThrowIfNull(p.Name);

            if (p.Type == Guid.Empty)
            {
                throw new ArgumentException($"Partition {i} has an empty type GUID.", nameof(partitions));
            }

            if (p.Name.Length > MaxNameChars)
            {
                throw new ArgumentException(
                    $"Partition {i} name has {p.Name.Length} UTF-16 characters; the maximum is {MaxNameChars}.", nameof(partitions));
            }

            if (p.FirstLba > p.LastLba)
            {
                throw new ArgumentException(
                    $"Partition {i} starts at LBA {p.FirstLba}, after its end at LBA {p.LastLba}.", nameof(partitions));
            }

            if (p.FirstLba < firstUsable || p.LastLba > lastUsable)
            {
                throw new ArgumentException(
                    $"Partition {i} ({p.FirstLba}-{p.LastLba}) lies outside the usable range {firstUsable}-{lastUsable}.",
                    nameof(partitions));
            }

            if (!uniqueIds.Add(p.UniqueId))
            {
                throw new ArgumentException($"Partition {i} repeats the unique GUID {p.UniqueId}.", nameof(partitions));
            }
        }

        // Overlap check: after sorting by start, any overlap shows up as a start at or before the furthest end so far.
        long furthestEnd = long.MinValue;
        foreach (GptPartitionEntry p in partitions.OrderBy(p => p.FirstLba))
        {
            if (p.FirstLba <= furthestEnd)
            {
                throw new ArgumentException(
                    $"Partition starting at LBA {p.FirstLba} overlaps another partition.", nameof(partitions));
            }

            furthestEnd = Math.Max(furthestEnd, p.LastLba);
        }
    }

    private static byte[] BuildEntryArray(IReadOnlyList<GptPartitionEntry> partitions)
    {
        byte[] array = new byte[EntryArrayBytes];
        for (int i = 0; i < partitions.Count; i++)
        {
            GptPartitionEntry p = partitions[i];
            Span<byte> entry = array.AsSpan(i * EntrySizeBytes, EntrySizeBytes);

            p.Type.ToByteArray().CopyTo(entry.Slice(TypeOffset, 16));
            p.UniqueId.ToByteArray().CopyTo(entry.Slice(UniqueIdOffset, 16));
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(FirstLbaOffset, 8), (ulong)p.FirstLba);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(LastLbaOffset, 8), (ulong)p.LastLba);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(AttributesOffset, 8), p.Attributes);

            // Validated to be at most 36 code units, that is 72 bytes of UTF-16LE. The rest of the field stays zero.
            Encoding.Unicode.GetBytes(p.Name).CopyTo(entry.Slice(NameOffset, NameSizeBytes));
        }

        return array;
    }

    /// <summary>
    /// Protective MBR in LBA 0: no boot code, one type 0xEE entry covering LBA 1 to the end of the disk, and the 0x55 0xAA
    /// signature at bytes 510 and 511.
    /// </summary>
    private static byte[] BuildProtectiveMbr(int sector, long lastLba)
    {
        byte[] image = new byte[sector];
        int e = MbrPartitionEntryOffset;

        image[e + 0] = 0x00; // status: not active
        image[e + 1] = 0x00; // CHS start: head 0
        image[e + 2] = 0x02; // CHS start: sector 2 (LBA 1)
        image[e + 3] = 0x00; // CHS start: cylinder 0
        image[e + 4] = 0xEE; // type: GPT protective
        image[e + 5] = 0xFF; // CHS end: maximum, as used above the CHS limit
        image[e + 6] = 0xFF;
        image[e + 7] = 0xFF;
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(e + 8, 4), 1u);
        BinaryPrimitives.WriteUInt32LittleEndian(image.AsSpan(e + 12, 4), (uint)Math.Min(lastLba, uint.MaxValue));

        image[510] = 0x55;
        image[511] = 0xAA;
        return image;
    }

    /// <summary>
    /// A GPT header in one sector. The first 92 bytes hold the header; the rest of the sector is zero. The CRC32 is computed
    /// over those 92 bytes with the CRC field itself set to zero.
    /// </summary>
    private static byte[] BuildHeader(
        int sector,
        long myLba,
        long alternateLba,
        long firstUsable,
        long lastUsable,
        Guid diskGuid,
        long entriesLba,
        uint entriesCrc)
    {
        byte[] image = new byte[sector];
        Span<byte> header = image.AsSpan(0, HeaderSizeBytes);

        "EFI PART"u8.CopyTo(header);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), Revision);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(12, 4), HeaderSizeBytes);
        // Offset 16: HeaderCRC32, still zero here. Offset 20: reserved, zero.
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(24, 8), (ulong)myLba);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(32, 8), (ulong)alternateLba);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(40, 8), (ulong)firstUsable);
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(48, 8), (ulong)lastUsable);
        diskGuid.ToByteArray().CopyTo(header.Slice(56, 16));
        BinaryPrimitives.WriteUInt64LittleEndian(header.Slice(72, 8), (ulong)entriesLba);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(80, 4), EntryCount);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(84, 4), EntrySizeBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(88, 4), entriesCrc);

        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(16, 4), Crc32.Compute(header));
        return image;
    }
}
