using System.Buffers.Binary;
using System.Text;
using WinCopy.Infrastructure.Windows.Gpt;
using Shouldly;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Gpt;

public sealed class GptSerializerTests
{
    private const long Mib = 1024L * 1024;
    private const long DiskBytes = 256 * Mib;
    private const long Lba512Last = DiskBytes / 512 - 1;
    private const int HeaderBytes = 92;
    private const int EntryArrayBytes = 16384;

    private static readonly Guid DiskGuid = new("11111111-2222-3333-4444-555555555555");
    private static readonly Guid BasicDataType = new("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7");
    private static readonly Guid UniqueA = new("AAAAAAAA-0000-0000-0000-000000000001");

    private static GptPartitionEntry DataPartition(
        long first = 2048, long last = 262143, string name = "Data", Guid? unique = null, ulong attributes = 0) =>
        new(BasicDataType, unique ?? UniqueA, first, last, attributes, name);

    private static GptDiskLayout Layout(params GptPartitionEntry[] partitions) =>
        new(DiskGuid, DiskBytes, 512, partitions);

    // ---------- Shape of the image ----------

    [Fact]
    public void Serialize512ByteDiskWritesFiveRecordsAtExpectedOffsets()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));

        image.Writes.Count.ShouldBe(5);
        image.Writes[0].Offset.ShouldBe(0L);
        image.Writes[0].Data.Length.ShouldBe(512);
        image.Writes[1].Offset.ShouldBe(512L);
        image.Writes[1].Data.Length.ShouldBe(512);
        image.Writes[2].Offset.ShouldBe(1024L);
        image.Writes[2].Data.Length.ShouldBe(EntryArrayBytes);
        image.Writes[3].Offset.ShouldBe(DiskBytes - 512 - EntryArrayBytes);
        image.Writes[3].Data.Length.ShouldBe(EntryArrayBytes);
        image.Writes[4].Offset.ShouldBe(DiskBytes - 512);
        image.Writes[4].Data.Length.ShouldBe(512);
    }

    [Fact]
    public void Serialize512ByteDiskUsableRangeIs34ToLastLbaMinus33()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));
        byte[] primary = image.Writes[1].Data;

        BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(40, 8)).ShouldBe(34UL);
        BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(48, 8)).ShouldBe((ulong)(Lba512Last - 33));
    }

    [Fact]
    public void SerializeBackupEntriesAreIdenticalToPrimaryEntries()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));

        Assert.Equal(image.Writes[2].Data, image.Writes[3].Data);
    }

    [Fact]
    public void SerializeThenSecondCallOnSameLayoutGivesIdenticalBytes()
    {
        GptDiskLayout layout = Layout(DataPartition());

        GptImage first = GptSerializer.Serialize(layout);
        GptImage second = GptSerializer.Serialize(layout);

        for (int i = 0; i < 5; i++)
        {
            second.Writes[i].Offset.ShouldBe(first.Writes[i].Offset);
            Assert.Equal(first.Writes[i].Data, second.Writes[i].Data);
        }
    }

    // ---------- Headers ----------

    [Fact]
    public void HeadersCarrySignatureRevisionSizeAndEntryTable()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));

        foreach (int index in new[] { 1, 4 })
        {
            byte[] header = image.Writes[index].Data;
            Encoding.ASCII.GetString(header, 0, 8).ShouldBe("EFI PART");
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4)).ShouldBe(0x00010000u);
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4)).ShouldBe(92u);
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(20, 4)).ShouldBe(0u);
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(80, 4)).ShouldBe(128u);
            BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(84, 4)).ShouldBe(128u);
            header.AsSpan(HeaderBytes).ToArray().ShouldAllBe(b => b == 0);
        }
    }

    [Fact]
    public void HeadersMyLbaAlternateLbaAndEntriesLbaAreCorrectInBothCopies()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));
        byte[] primary = image.Writes[1].Data;
        byte[] backup = image.Writes[4].Data;

        BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(24, 8)).ShouldBe(1UL);
        BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(32, 8)).ShouldBe((ulong)Lba512Last);
        BinaryPrimitives.ReadUInt64LittleEndian(primary.AsSpan(72, 8)).ShouldBe(2UL);

        BinaryPrimitives.ReadUInt64LittleEndian(backup.AsSpan(24, 8)).ShouldBe((ulong)Lba512Last);
        BinaryPrimitives.ReadUInt64LittleEndian(backup.AsSpan(32, 8)).ShouldBe(1UL);
        BinaryPrimitives.ReadUInt64LittleEndian(backup.AsSpan(72, 8)).ShouldBe((ulong)(Lba512Last - 32));
    }

    [Fact]
    public void HeadersCarryDiskGuidInMixedEndianLayout()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));

        Assert.Equal(DiskGuid.ToByteArray(), image.Writes[1].Data.AsSpan(56, 16).ToArray());
        Assert.Equal(DiskGuid.ToByteArray(), image.Writes[4].Data.AsSpan(56, 16).ToArray());
    }

    [Fact]
    public void HeadersHeaderCrcVerifiesWithFieldZeroed()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));

        AssertHeaderCrc(image.Writes[1].Data);
        AssertHeaderCrc(image.Writes[4].Data);
    }

    [Fact]
    public void HeadersEntriesCrcMatchesEntryArrayInBothCopies()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));
        uint expected = Crc32.Compute(image.Writes[2].Data);

        BinaryPrimitives.ReadUInt32LittleEndian(image.Writes[1].Data.AsSpan(88, 4)).ShouldBe(expected);
        BinaryPrimitives.ReadUInt32LittleEndian(image.Writes[4].Data.AsSpan(88, 4)).ShouldBe(expected);
        Crc32.Compute(image.Writes[3].Data).ShouldBe(expected);
    }

    [Fact]
    public void HeadersPrimaryAndBackupDifferOnlyInMyLbaAlternateLbaEntriesLbaAndCrc()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));
        byte[] primary = image.Writes[1].Data.AsSpan(0, HeaderBytes).ToArray();
        byte[] backup = image.Writes[4].Data.AsSpan(0, HeaderBytes).ToArray();

        List<int> differing = Enumerable.Range(0, HeaderBytes).Where(i => primary[i] != backup[i]).ToList();
        HashSet<int> allowed = Enumerable.Range(16, 4)
            .Concat(Enumerable.Range(24, 16))
            .Concat(Enumerable.Range(72, 8))
            .ToHashSet();

        differing.ShouldContain(24);
        differing.ShouldAllBe(i => allowed.Contains(i));
    }

    // ---------- Partition entries ----------

    [Fact]
    public void EntryFieldsAndNameAreSerializedAtSpecOffsets()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition(attributes: 0x8000000000000001UL)));
        byte[] entries = image.Writes[2].Data;

        Assert.Equal(BasicDataType.ToByteArray(), entries.AsSpan(0, 16).ToArray());
        Assert.Equal(UniqueA.ToByteArray(), entries.AsSpan(16, 16).ToArray());
        BinaryPrimitives.ReadUInt64LittleEndian(entries.AsSpan(32, 8)).ShouldBe(2048UL);
        BinaryPrimitives.ReadUInt64LittleEndian(entries.AsSpan(40, 8)).ShouldBe(262143UL);
        BinaryPrimitives.ReadUInt64LittleEndian(entries.AsSpan(48, 8)).ShouldBe(0x8000000000000001UL);
        Assert.Equal(PadName("Data", 72), entries.AsSpan(56, 72).ToArray());
    }

    [Fact]
    public void EntriesUnusedSlotsAreZeroAndPartitionsKeepInputOrder()
    {
        var second = new GptPartitionEntry(
            BasicDataType, new Guid("BBBBBBBB-0000-0000-0000-000000000002"), 300000, 300999, 0, "Second");
        GptImage image = GptSerializer.Serialize(Layout(DataPartition(), second));
        byte[] entries = image.Writes[2].Data;

        BinaryPrimitives.ReadUInt64LittleEndian(entries.AsSpan(128 + 32, 8)).ShouldBe(300000UL);
        entries.AsSpan(256).ToArray().ShouldAllBe(b => b == 0);
    }

    [Fact]
    public void NameThirtySixCharactersIsAccepted()
    {
        string name = new('W', 36);

        GptImage image = GptSerializer.Serialize(Layout(DataPartition(name: name)));

        Assert.Equal(Encoding.Unicode.GetBytes(name), image.Writes[2].Data.AsSpan(56, 72).ToArray());
    }

    [Fact]
    public void NameThirtySevenCharactersIsRejected()
    {
        GptDiskLayout layout = Layout(DataPartition(name: new string('W', 37)));

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    // ---------- Validation ----------

    [Theory]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(0)]
    public void UnsupportedSectorSizeIsRejected(int sectorSize)
    {
        var layout = new GptDiskLayout(DiskGuid, DiskBytes, sectorSize, [DataPartition()]);

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void DiskSizeNotMultipleOfSectorIsRejected()
    {
        var layout = new GptDiskLayout(DiskGuid, DiskBytes + 1, 512, [DataPartition()]);

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void OverlappingPartitionsAreRejected()
    {
        GptDiskLayout layout = Layout(
            DataPartition(first: 2048, last: 4095),
            DataPartition(first: 4000, last: 6000, unique: new Guid("CCCCCCCC-0000-0000-0000-000000000003")));

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void PartitionStartingBeforeFirstUsableLbaIsRejected()
    {
        GptDiskLayout layout = Layout(DataPartition(first: 33, last: 2048));

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void PartitionEndingAfterLastUsableLbaIsRejected()
    {
        long lastUsable = Lba512Last - 33;
        GptDiskLayout layout = Layout(DataPartition(first: 2048, last: lastUsable + 1));

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void PartitionEndingAtLastUsableLbaIsAccepted()
    {
        long lastUsable = Lba512Last - 33;

        GptImage image = GptSerializer.Serialize(Layout(DataPartition(first: 2048, last: lastUsable)));

        BinaryPrimitives.ReadUInt64LittleEndian(image.Writes[2].Data.AsSpan(40, 8)).ShouldBe((ulong)lastUsable);
    }

    [Fact]
    public void FirstLbaAfterLastLbaIsRejected()
    {
        GptDiskLayout layout = Layout(DataPartition(first: 4096, last: 2048));

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void DuplicateUniqueIdIsRejected()
    {
        GptDiskLayout layout = Layout(
            DataPartition(first: 2048, last: 4095),
            DataPartition(first: 8192, last: 9000));

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void EmptyTypeGuidIsRejected()
    {
        var entry = new GptPartitionEntry(Guid.Empty, UniqueA, 2048, 4095, 0, "Data");

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(Layout(entry)));
    }

    [Fact]
    public void MoreThan128PartitionsIsRejected()
    {
        GptDiskLayout layout = Layout(ManySmallPartitions(129));

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    [Fact]
    public void Exactly128PartitionsIsAccepted()
    {
        GptImage image = GptSerializer.Serialize(Layout(ManySmallPartitions(128)));

        BinaryPrimitives.ReadUInt64LittleEndian(image.Writes[2].Data.AsSpan(127 * 128 + 32, 8))
            .ShouldBe((ulong)(2048 + 127 * 2));
    }

    // ---------- Protective MBR ----------

    [Fact]
    public void ProtectiveMbrHasOnlyTheProtectiveEntryAndSignature()
    {
        GptImage image = GptSerializer.Serialize(Layout(DataPartition()));
        byte[] mbr = image.Writes[0].Data;

        mbr.Length.ShouldBe(512);
        mbr.AsSpan(0, 446).ToArray().ShouldAllBe(b => b == 0);

        mbr[446].ShouldBe((byte)0x00); // status
        mbr[447].ShouldBe((byte)0x00); // CHS start head
        mbr[448].ShouldBe((byte)0x02); // CHS start sector
        mbr[449].ShouldBe((byte)0x00); // CHS start cylinder
        mbr[450].ShouldBe((byte)0xEE); // type: GPT protective
        mbr[451].ShouldBe((byte)0xFF); // CHS end
        mbr[452].ShouldBe((byte)0xFF);
        mbr[453].ShouldBe((byte)0xFF);
        BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(454, 4)).ShouldBe(1u); // StartingLBA
        BinaryPrimitives.ReadUInt32LittleEndian(mbr.AsSpan(458, 4)).ShouldBe((uint)Lba512Last); // SizeInLBA

        mbr.AsSpan(462, 48).ToArray().ShouldAllBe(b => b == 0);
        mbr[510].ShouldBe((byte)0x55);
        mbr[511].ShouldBe((byte)0xAA);
    }

    [Fact]
    public void ProtectiveMbrSizeIsClampedToUInt32MaxForHugeDisks()
    {
        const long fourTib = 4L * 1024 * 1024 * 1024 * 1024;
        var layout = new GptDiskLayout(DiskGuid, fourTib, 512, [DataPartition(last: 262143)]);

        GptImage image = GptSerializer.Serialize(layout);

        BinaryPrimitives.ReadUInt32LittleEndian(image.Writes[0].Data.AsSpan(458, 4)).ShouldBe(uint.MaxValue);
    }

    // ---------- 4096-byte logical sectors ----------

    [Fact]
    public void FourKDiskEntriesSpanFourSectorsAndFirstUsableIsSix()
    {
        const long size = 256 * Mib;
        const long lastLba = size / 4096 - 1;
        var layout = new GptDiskLayout(DiskGuid, size, 4096, [DataPartition(first: 2048, last: 4095)]);

        GptImage image = GptSerializer.Serialize(layout);

        image.Writes.Count.ShouldBe(5);
        image.Writes[0].Data.Length.ShouldBe(4096);
        image.Writes[1].Offset.ShouldBe(4096L);
        image.Writes[1].Data.Length.ShouldBe(4096);
        image.Writes[2].Offset.ShouldBe(8192L);
        image.Writes[2].Data.Length.ShouldBe(EntryArrayBytes);
        image.Writes[3].Offset.ShouldBe((lastLba - 4) * 4096);
        image.Writes[3].Data.Length.ShouldBe(EntryArrayBytes);
        image.Writes[4].Offset.ShouldBe(size - 4096);
        image.Writes[4].Data.Length.ShouldBe(4096);

        BinaryPrimitives.ReadUInt64LittleEndian(image.Writes[1].Data.AsSpan(40, 8)).ShouldBe(6UL);
        BinaryPrimitives.ReadUInt64LittleEndian(image.Writes[1].Data.AsSpan(48, 8)).ShouldBe((ulong)(lastLba - 5));
        BinaryPrimitives.ReadUInt64LittleEndian(image.Writes[4].Data.AsSpan(72, 8)).ShouldBe((ulong)(lastLba - 4));
        AssertHeaderCrc(image.Writes[1].Data);
        AssertHeaderCrc(image.Writes[4].Data);
    }

    [Fact]
    public void FourKDiskPartitionBelowSixIsRejected()
    {
        const long size = 256 * Mib;
        var layout = new GptDiskLayout(DiskGuid, size, 4096, [DataPartition(first: 5, last: 4095)]);

        Assert.Throws<ArgumentException>(() => GptSerializer.Serialize(layout));
    }

    // ---------- Golden comparison with an independent re-derivation ----------

    [Fact]
    public void Golden512ByteDiskMatchesIndependentlyDerivedBytes()
    {
        GptDiskLayout layout = Layout(DataPartition());

        GptImage actual = GptSerializer.Serialize(layout);
        IReadOnlyList<(long Offset, byte[] Data)> expected = ReferenceGpt.Build512(
            DiskGuid, DiskBytes, BasicDataType, UniqueA, 2048, 262143, "Data");

        actual.Writes.Count.ShouldBe(expected.Count);
        for (int i = 0; i < expected.Count; i++)
        {
            actual.Writes[i].Offset.ShouldBe(expected[i].Offset);
            Assert.Equal(expected[i].Data, actual.Writes[i].Data);
        }
    }

    // ---------- helpers ----------

    private static byte[] PadName(string name, int size)
    {
        byte[] result = new byte[size];
        Encoding.Unicode.GetBytes(name).CopyTo(result, 0);
        return result;
    }

    private static GptPartitionEntry[] ManySmallPartitions(int count)
    {
        var entries = new GptPartitionEntry[count];
        for (int i = 0; i < count; i++)
        {
            long first = 2048 + i * 2L;
            entries[i] = new GptPartitionEntry(
                BasicDataType, new Guid(i + 1, 0, 0, new byte[8]), first, first, 0, "P");
        }

        return entries;
    }

    private static void AssertHeaderCrc(byte[] sector)
    {
        byte[] copy = sector.AsSpan(0, HeaderBytes).ToArray();
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(copy.AsSpan(16, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(copy.AsSpan(16, 4), 0);
        Crc32.Compute(copy).ShouldBe(stored);
    }

    /// <summary>
    /// A second, deliberately naive derivation of the 512-byte layout (bitwise CRC, explicit offsets). It shares no code
    /// with <see cref="GptSerializer"/>, so a byte-for-byte match is evidence that the serializer follows the layout.
    /// </summary>
    private static class ReferenceGpt
    {
        public static IReadOnlyList<(long Offset, byte[] Data)> Build512(
            Guid diskGuid, long sizeBytes, Guid type, Guid unique, long first, long last, string name)
        {
            const int sector = 512;
            long lastLba = sizeBytes / sector - 1;
            const long entriesSectors = 32;

            byte[] entries = new byte[16384];
            type.ToByteArray().CopyTo(entries, 0);
            unique.ToByteArray().CopyTo(entries, 16);
            BinaryPrimitives.WriteUInt64LittleEndian(entries.AsSpan(32, 8), (ulong)first);
            BinaryPrimitives.WriteUInt64LittleEndian(entries.AsSpan(40, 8), (ulong)last);
            Encoding.Unicode.GetBytes(name).CopyTo(entries, 56);
            uint entriesCrc = Bitwise(entries);

            byte[] mbr = new byte[sector];
            mbr[450] = 0xEE;
            mbr[451] = 0xFF;
            mbr[452] = 0xFF;
            mbr[453] = 0xFF;
            mbr[448] = 0x02;
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(454, 4), 1);
            BinaryPrimitives.WriteUInt32LittleEndian(mbr.AsSpan(458, 4), (uint)Math.Min(lastLba, 0xFFFFFFFFL));
            mbr[510] = 0x55;
            mbr[511] = 0xAA;

            byte[] primary = Header(diskGuid, 1, lastLba, 34, lastLba - 33, 2, entriesCrc);
            byte[] backup = Header(diskGuid, lastLba, 1, 34, lastLba - 33, lastLba - entriesSectors, entriesCrc);

            return
            [
                (0L, mbr),
                (512L, primary),
                (1024L, entries),
                ((lastLba - entriesSectors) * sector, entries),
                (lastLba * sector, backup),
            ];
        }

        private static byte[] Header(Guid diskGuid, long myLba, long alternateLba, long firstUsable, long lastUsable, long entriesLba, uint entriesCrc)
        {
            byte[] sector = new byte[512];
            "EFI PART"u8.CopyTo(sector);
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(8, 4), 0x00010000);
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(12, 4), 92);
            BinaryPrimitives.WriteUInt64LittleEndian(sector.AsSpan(24, 8), (ulong)myLba);
            BinaryPrimitives.WriteUInt64LittleEndian(sector.AsSpan(32, 8), (ulong)alternateLba);
            BinaryPrimitives.WriteUInt64LittleEndian(sector.AsSpan(40, 8), (ulong)firstUsable);
            BinaryPrimitives.WriteUInt64LittleEndian(sector.AsSpan(48, 8), (ulong)lastUsable);
            diskGuid.ToByteArray().CopyTo(sector, 56);
            BinaryPrimitives.WriteUInt64LittleEndian(sector.AsSpan(72, 8), (ulong)entriesLba);
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(80, 4), 128);
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(84, 4), 128);
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(88, 4), entriesCrc);
            BinaryPrimitives.WriteUInt32LittleEndian(sector.AsSpan(16, 4), Bitwise(sector.AsSpan(0, 92).ToArray()));
            return sector;
        }

        private static uint Bitwise(ReadOnlySpan<byte> data)
        {
            uint crc = 0xFFFFFFFFu;
            foreach (byte b in data)
            {
                crc ^= b;
                for (int k = 0; k < 8; k++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
                }
            }

            return ~crc;
        }
    }
}
