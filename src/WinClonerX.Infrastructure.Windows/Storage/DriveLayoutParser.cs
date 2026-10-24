using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.Storage;

/// <summary>
/// Parses the reply of <c>IOCTL_DISK_GET_DRIVE_LAYOUT_EX</c>. It is pure: it reads bytes and never touches a device,
/// so the layout rules can be tested with byte arrays.
/// </summary>
internal static class DriveLayoutParser
{
    /// <summary>GPT tables hold at most 128 entries by default. The reply buffer is sized for that many.</summary>
    internal const int MaxGptPartitions = 128;

    /// <summary>Size of the DRIVE_LAYOUT_INFORMATION_EX header, which is followed by the partition entries.</summary>
    internal static readonly int HeaderSize = Unsafe.SizeOf<DRIVE_LAYOUT_INFORMATION_EX>();

    /// <summary>Size of the reply buffer: the header plus room for <see cref="MaxGptPartitions"/> entries.</summary>
    internal static readonly int ReplyBufferSize = HeaderSize + (MaxGptPartitions * IoControlConstants.PARTITION_INFORMATION_EX_SIZE);

    // PARTITION_STYLE_GPT in winioctl.h (MBR = 0, GPT = 1, RAW = 2). This is not the MSFT_Disk value.
    private const uint PartitionStyleGpt = 1;

    // Within one PARTITION_INFORMATION_EX: the GPT union starts at 32, Attributes at +32 and Name at +40 of the union.
    private const int AttributesOffsetInEntry = 64;
    private const int NameOffsetInEntry = 72;
    private const int NameByteLength = 72;

    /// <summary>Reads the GPT entries from a reply. Throws when the reply is short, truncated or not GPT.</summary>
    public static IReadOnlyList<DriveLayoutEntry> Parse(ReadOnlySpan<byte> reply)
    {
        if (reply.Length < HeaderSize)
        {
            throw new InvalidOperationException($"Drive layout reply is too short ({reply.Length} bytes).");
        }

        DRIVE_LAYOUT_INFORMATION_EX header = MemoryMarshal.Read<DRIVE_LAYOUT_INFORMATION_EX>(reply);
        if (header.PartitionStyle != PartitionStyleGpt)
        {
            throw new InvalidOperationException("Drive layout is not GPT.");
        }

        uint count = header.PartitionCount;
        if (HeaderSize + ((long)count * IoControlConstants.PARTITION_INFORMATION_EX_SIZE) > reply.Length)
        {
            throw new InvalidOperationException($"Drive layout reply is truncated for {count} partitions.");
        }

        var entries = new List<DriveLayoutEntry>((int)count);
        for (int i = 0; i < count; i++)
        {
            ReadOnlySpan<byte> entry = reply.Slice(
                HeaderSize + (i * IoControlConstants.PARTITION_INFORMATION_EX_SIZE),
                IoControlConstants.PARTITION_INFORMATION_EX_SIZE);

            PARTITION_INFORMATION_EX info = MemoryMarshal.Read<PARTITION_INFORMATION_EX>(entry);
            entries.Add(new DriveLayoutEntry(info.PartitionNumber, ReadAttributes(entry), ReadName(entry)));
        }

        return entries;
    }

    // Read from the raw bytes at the documented offset, which is the same as the struct field.
    private static ulong ReadAttributes(ReadOnlySpan<byte> entry) =>
        MemoryMarshal.Read<ulong>(entry.Slice(AttributesOffsetInEntry, sizeof(ulong)));

    private static string ReadName(ReadOnlySpan<byte> entry)
    {
        string text = Encoding.Unicode.GetString(entry.Slice(NameOffsetInEntry, NameByteLength));
        int terminator = text.IndexOf('\0', StringComparison.Ordinal);
        return terminator >= 0 ? text[..terminator] : text;
    }
}
