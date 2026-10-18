using System.Runtime.InteropServices;
using Shouldly;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.RawIo;

public sealed class VolumeBitmapParserTests
{
    private const int OneMiB = 1024 * 1024;

    // A 930 GiB volume at 4 KiB clusters: BitmapSize counts its clusters from the chunk to the end of the volume.
    private const long RealBitmapSize = 243_898_000;

    private static readonly int Header = VolumeBitmapParser.HeaderBytes;

    [Fact]
    public void HeaderIsSixteenBytesStartingLcnAndBitmapSize()
    {
        Header.ShouldBe(16);
    }

    [Fact]
    public void CompleteChunkIsReadWithItsExactClusterCount()
    {
        // 20 clusters from cluster 0 need 3 bytes. Clusters 0, 9 and 19 are used.
        byte[] buffer = new byte[Header + 3];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: 20);
        buffer[Header] = 0x01;
        buffer[Header + 1] = 0x02;
        buffer[Header + 2] = 0x08;

        Result result = Parse(buffer, returned: (uint)buffer.Length, moreData: false);

        result.Bytes.ShouldBe(3);
        result.StartingLcn.ShouldBe(0);
        result.Clusters.ShouldBe(20);
        buffer.AsSpan(0, 3).ToArray().ShouldBe(new byte[] { 0x01, 0x02, 0x08 });
    }

    [Fact]
    public void CompleteChunkWithPaddingBitsCoversOnlyBitmapSizeClusters()
    {
        // 13 clusters need 2 bytes. The 3 padding bits of the last byte are not clusters of this chunk.
        byte[] buffer = new byte[Header + 2];
        WriteHeader(buffer, startingLcn: 8, bitmapSize: 13);
        buffer[Header] = 0xFF;
        buffer[Header + 1] = 0xFF;

        Result result = Parse(buffer, returned: (uint)buffer.Length, moreData: false);

        result.Bytes.ShouldBe(2);
        result.StartingLcn.ShouldBe(8);
        result.Clusters.ShouldBe(13);
    }

    [Fact]
    public void PartialChunkOfARealVolumeCoversTheBitsThatFitAndMovesThemToTheStart()
    {
        // The real case: BitmapSize is 243,898,000 clusters, but a 1 MiB buffer holds only the first part.
        byte[] buffer = new byte[OneMiB];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: RealBitmapSize);
        for (int i = 0; i < OneMiB - Header; i++)
        {
            buffer[Header + i] = (byte)(i * 7 + 3);
        }

        Result result = Parse(buffer, returned: OneMiB, moreData: true);

        long expectedBytes = OneMiB - Header;
        result.Bytes.ShouldBe((int)expectedBytes);
        result.StartingLcn.ShouldBe(0);
        result.Clusters.ShouldBe(expectedBytes * 8);
        for (int i = 0; i < expectedBytes; i++)
        {
            buffer[i].ShouldBe((byte)(i * 7 + 3));
        }
    }

    [Fact]
    public void ZeroReportedBytesOnMoreDataTakesTheWholeBuffer()
    {
        // The driver did not set the byte count on ERROR_MORE_DATA, so the full buffer is taken as present.
        byte[] buffer = new byte[OneMiB];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: RealBitmapSize);

        Result result = Parse(buffer, returned: 0, moreData: true);

        result.Bytes.ShouldBe(OneMiB - Header);
        result.Clusters.ShouldBe((long)(OneMiB - Header) * 8);
    }

    [Fact]
    public void ReportedByteCountBelowTheBufferLimitsThePresentBits()
    {
        // The reported count is used when it is set: 800 bitmap bytes are present, not the whole buffer.
        byte[] buffer = new byte[OneMiB];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: RealBitmapSize);

        Result result = Parse(buffer, returned: (uint)(Header + 800), moreData: true);

        result.Bytes.ShouldBe(800);
        result.Clusters.ShouldBe(800L * 8);
    }

    [Fact]
    public void ReturnsTheStartingLcnFromTheHeaderWhenTheDeviceRoundedItDown()
    {
        // The device rounds the start down to a multiple of 8. The caller skips the clusters below its request.
        byte[] buffer = new byte[Header + 4];
        WriteHeader(buffer, startingLcn: 16, bitmapSize: 32);

        Result result = Parse(buffer, returned: (uint)buffer.Length, moreData: false);

        result.StartingLcn.ShouldBe(16);
        result.Clusters.ShouldBe(32);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void BitmapSizeNotPositiveFailsClosed(long bitmapSize)
    {
        byte[] buffer = new byte[Header + 4];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: bitmapSize);

        Should.Throw<InvalidOperationException>(() => { Parse(buffer, returned: (uint)buffer.Length, moreData: false); });
    }

    [Fact]
    public void ReturnedBytesBeyondTheBufferFailClosed()
    {
        byte[] buffer = new byte[Header + 4];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: 32);

        Should.Throw<InvalidOperationException>(() => { Parse(buffer, returned: (uint)buffer.Length + 1, moreData: true); });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReturnedBytesNotLargerThanTheHeaderFailClosed(bool moreData)
    {
        byte[] buffer = new byte[Header + 4];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: 32);

        Should.Throw<InvalidOperationException>(() => { Parse(buffer, returned: (uint)Header, moreData: moreData); });
    }

    [Fact]
    public void FewerBytesThanBitmapSizeNeedsWithoutMoreDataFailClosed()
    {
        // BitmapSize 100 needs 13 bytes, but only 5 bytes came back and the call did not report more data.
        byte[] buffer = new byte[Header + 13];
        WriteHeader(buffer, startingLcn: 0, bitmapSize: 100);

        Should.Throw<InvalidOperationException>(() => { Parse(buffer, returned: (uint)(Header + 5), moreData: false); });
    }

    [Fact]
    public void BufferNoLargerThanTheHeaderIsAnArgumentError()
    {
        byte[] buffer = new byte[Header];

        Should.Throw<ArgumentException>(() => { Parse(buffer, returned: (uint)Header, moreData: true); });
    }

    private static void WriteHeader(byte[] buffer, long startingLcn, long bitmapSize)
    {
        MemoryMarshal.Write(buffer, new VOLUME_BITMAP_BUFFER { StartingLcn = startingLcn, BitmapSize = bitmapSize });
    }

    private static Result Parse(byte[] buffer, uint returned, bool moreData)
    {
        int bytes = VolumeBitmapParser.Parse(buffer, returned, moreData, out long startingLcn, out long clusters);
        return new Result(bytes, startingLcn, clusters);
    }

    private sealed record Result(int Bytes, long StartingLcn, long Clusters);
}
