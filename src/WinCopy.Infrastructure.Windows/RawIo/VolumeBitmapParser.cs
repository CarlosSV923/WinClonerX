using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Parses one output buffer of FSCTL_GET_VOLUME_BITMAP. The output is a <c>VOLUME_BITMAP_BUFFER</c> header followed by
/// the bitmap bits, least significant bit first.
/// </summary>
/// <remarks>
/// <para>
/// <c>BitmapSize</c> is the number of clusters from <c>StartingLcn</c> to the END OF THE VOLUME. It is not the number of
/// clusters that fit in the buffer. A 1 MiB buffer holds about 8.4 million clusters, while a 930 GiB volume at 4 KiB
/// clusters has about 244 million, so a chunk is usually much shorter than <c>BitmapSize</c>.
/// </para>
/// <para>
/// When the buffer is too small, DeviceIoControl fails with ERROR_MORE_DATA (234). The buffer then holds as many bitmap
/// bytes as fit after the header. The returned <c>StartingLcn</c> may be rounded down to a multiple of 8 below the
/// requested cluster.
/// </para>
/// </remarks>
internal static class VolumeBitmapParser
{
    /// <summary>Size of the VOLUME_BITMAP_BUFFER header: StartingLcn and BitmapSize, 16 bytes.</summary>
    internal static readonly int HeaderBytes = Unsafe.SizeOf<VOLUME_BITMAP_BUFFER>();

    /// <summary>
    /// Reads the header, moves the bitmap bits to the start of <paramref name="output"/> and returns how many bytes of bits
    /// it holds. Fails closed on any header or length that does not match the contract.
    /// </summary>
    /// <param name="output">The whole output buffer that was passed to DeviceIoControl.</param>
    /// <param name="returnedBytes">The byte count that DeviceIoControl reported (lpBytesReturned).</param>
    /// <param name="moreData">True when the call failed with ERROR_MORE_DATA, so more of the bitmap remains.</param>
    /// <param name="chunkStartingLcn">The first cluster the chunk covers. It can be below the requested cluster.</param>
    /// <param name="chunkClusters">
    /// The clusters covered by this chunk, from <paramref name="chunkStartingLcn"/>. This is NOT <c>BitmapSize</c>. It is
    /// the smaller of <c>BitmapSize</c> and the bits present, so the next chunk starts at
    /// <c>chunkStartingLcn + chunkClusters</c>.
    /// </param>
    /// <returns>The number of bitmap bytes now at the start of <paramref name="output"/>.</returns>
    /// <exception cref="ArgumentException">The output buffer is not larger than the header.</exception>
    /// <exception cref="InvalidOperationException">The header or the byte count does not match the contract.</exception>
    internal static int Parse(
        Span<byte> output, uint returnedBytes, bool moreData, out long chunkStartingLcn, out long chunkClusters)
    {
        chunkStartingLcn = 0;
        chunkClusters = 0;

        if (output.Length <= HeaderBytes)
        {
            throw new ArgumentException("The output buffer is too small for a volume bitmap.", nameof(output));
        }

        // The reported byte count is used when DeviceIoControl sets it. The one case where it is not trusted is
        // ERROR_MORE_DATA with a count of 0. A driver may leave lpBytesReturned unset when the buffer overflows, and
        // ERROR_MORE_DATA means the buffer was filled, so the whole output buffer is taken as present.
        long reported = moreData && returnedBytes == 0 ? output.Length : returnedBytes;
        if (reported <= HeaderBytes || reported > output.Length)
        {
            throw new InvalidOperationException(
                $"FSCTL_GET_VOLUME_BITMAP returned {reported} bytes for a buffer of {output.Length} bytes.");
        }

        VOLUME_BITMAP_BUFFER header = MemoryMarshal.Read<VOLUME_BITMAP_BUFFER>(output);
        if (header.BitmapSize <= 0 || header.StartingLcn < 0)
        {
            throw new InvalidOperationException(
                $"Unexpected volume bitmap header at cluster {header.StartingLcn}: BitmapSize is {header.BitmapSize}.");
        }

        // BitmapSize counts clusters to the end of the volume, so the chunk needs ceil(BitmapSize / 8) bytes in total.
        // Written as (BitmapSize - 1) / 8 + 1 to avoid an overflow when BitmapSize is near long.MaxValue.
        long neededBytes = (header.BitmapSize - 1) / 8 + 1;
        long presentBytes = Math.Min(neededBytes, reported - HeaderBytes);
        if (!moreData && presentBytes != neededBytes)
        {
            throw new InvalidOperationException(
                $"The volume bitmap at cluster {header.StartingLcn} holds {presentBytes} of the {neededBytes} bytes it needs.");
        }

        // A chunk with more data after it holds whole bytes only (presentBytes is less than neededBytes), so its cluster
        // count is a multiple of 8. A final chunk covers the rest of BitmapSize, including the padding bits of the last byte.
        chunkClusters = Math.Min(header.BitmapSize, presentBytes * 8);
        chunkStartingLcn = header.StartingLcn;

        // The bits follow the header. Span.CopyTo handles the overlap, so the bits move to the start of the output.
        output.Slice(HeaderBytes, (int)presentBytes).CopyTo(output);
        return (int)presentBytes;
    }
}
