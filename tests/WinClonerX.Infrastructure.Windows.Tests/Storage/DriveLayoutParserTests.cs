using System.Buffers.Binary;
using System.Text;
using Shouldly;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.Storage;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Storage;

// The reply is built as bytes, so the layout rules are checked without any device.
public sealed class DriveLayoutParserTests
{
    private const int EntrySize = 144;
    private const int AttributesOffset = 64;
    private const int NameOffset = 72;

    [Fact]
    public void ParsesEachEntrysNumberAttributesAndName()
    {
        byte[] reply = BuildGptReply(
            (1, 0x0UL, "EFI system partition"),
            (3, 0x8000000000000001UL, "Basic data partition"));

        IReadOnlyList<DriveLayoutEntry> entries = DriveLayoutParser.Parse(reply);

        entries.ShouldBe(
        [
            new DriveLayoutEntry(1, 0x0UL, "EFI system partition"),
            new DriveLayoutEntry(3, 0x8000000000000001UL, "Basic data partition"),
        ]);
    }

    [Fact]
    public void TheNameEndsAtTheFirstNulCharacter()
    {
        byte[] reply = BuildGptReply((2, 0UL, "Recovery\0leftover"));

        DriveLayoutParser.Parse(reply).Single().Name.ShouldBe("Recovery");
    }

    [Fact]
    public void ATableWithNoPartitionsGivesNoEntries()
    {
        DriveLayoutParser.Parse(BuildGptReply()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(2u)]
    public void ANonGptLayoutIsRefused(uint style)
    {
        byte[] reply = BuildReply(style, (1, 0UL, "Any"));

        Should.Throw<InvalidOperationException>(() => DriveLayoutParser.Parse(reply));
    }

    [Fact]
    public void AShortReplyIsRefused()
    {
        Should.Throw<InvalidOperationException>(() => DriveLayoutParser.Parse(new byte[DriveLayoutParser.HeaderSize - 1]));
    }

    [Fact]
    public void AReplyThatIsTooShortForItsOwnCountIsRefused()
    {
        byte[] reply = BuildGptReply((1, 0UL, "One"));
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(4), 3);

        Should.Throw<InvalidOperationException>(() => DriveLayoutParser.Parse(reply.AsSpan(0, DriveLayoutParser.HeaderSize + EntrySize)));
    }

    [Fact]
    public void TheReplyBufferHoldsTheHeaderAnd128Entries()
    {
        DriveLayoutParser.HeaderSize.ShouldBe(48);
        DriveLayoutParser.MaxGptPartitions.ShouldBe(128);
        DriveLayoutParser.ReplyBufferSize.ShouldBe(48 + (128 * 144));
    }

    /// <summary>A GPT reply with the given entries. Layout: 48-byte header, then 144-byte PARTITION_INFORMATION_EX entries.</summary>
    private static byte[] BuildGptReply(params (uint Number, ulong Attributes, string Name)[] entries) =>
        BuildReply(1, entries);

    private static byte[] BuildReply(uint style, params (uint Number, ulong Attributes, string Name)[] entries)
    {
        byte[] reply = new byte[DriveLayoutParser.HeaderSize + (entries.Length * EntrySize)];
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(0), style);
        BinaryPrimitives.WriteUInt32LittleEndian(reply.AsSpan(4), (uint)entries.Length);

        for (int i = 0; i < entries.Length; i++)
        {
            int start = DriveLayoutParser.HeaderSize + (i * EntrySize);
            Span<byte> entry = reply.AsSpan(start, EntrySize);

            BinaryPrimitives.WriteUInt32LittleEndian(entry.Slice(24), entries[i].Number);
            BinaryPrimitives.WriteUInt64LittleEndian(entry.Slice(AttributesOffset), entries[i].Attributes);
            Encoding.Unicode.GetBytes(entries[i].Name, entry.Slice(NameOffset, 72));
        }

        return reply;
    }
}
