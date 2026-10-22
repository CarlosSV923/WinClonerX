using System.ComponentModel;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// The device is replaced by a fake. No test opens a physical disk.
public sealed class RawPartitionReaderTests
{
    private const long MiB = 1L << 20;
    private const int Sector = 512;

    private static readonly DiskId Source = new(3, Guid.Parse("aaaaaaaa-0000-0000-0000-000000000003"));

    [Fact]
    public void OpensThePhysicalDriveWithGenericReadOnly()
    {
        var opener = new FakeOpener();

        _ = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 4 * MiB));

        RawDeviceOpenRequest request = opener.Requests.Single();
        request.DevicePath.ShouldBe(@"\\.\PhysicalDrive3");
        request.DesiredAccess.ShouldBe(Kernel32.GENERIC_READ);
        (request.DesiredAccess & Kernel32.GENERIC_WRITE).ShouldBe(0u);
        request.ShareMode.ShouldBe(Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE);
        request.CreationDisposition.ShouldBe(Kernel32.OPEN_EXISTING);
        (request.Flags & Kernel32.FILE_FLAG_NO_BUFFERING).ShouldNotBe(0u);
    }

    [Fact]
    public void ExposesTheExtentLengthAsTheSourceLength()
    {
        IBlockSource source = new RawPartitionReader(new FakeOpener()).Open(Source, new Extent(MiB, 4 * MiB));

        source.Length.ShouldBe(new ByteSize(4 * MiB));
    }

    [Fact]
    public async Task ReturnsOneRangeCoveringTheWholeExtentWithSectorClusters()
    {
        IBlockSource source = new RawPartitionReader(new FakeOpener()).Open(Source, new Extent(MiB, 4 * MiB));

        VolumeAllocation allocation = await source.GetAllocationAsync(CancellationToken.None);

        allocation.ClusterSize.ShouldBe(new ByteSize(Sector));
        allocation.UsedRanges.ShouldBe([new Extent(0, 4 * MiB)]);
    }

    [Fact]
    public async Task ReadsAtTheExtentOffsetPlusTheRequestedOffset()
    {
        var opener = new FakeOpener();
        IBlockSource source = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 4 * MiB));

        int read = await source.ReadAsync(4096, new byte[8192], CancellationToken.None);

        read.ShouldBe(8192);
        opener.Device.Reads.ShouldBe([(MiB + 4096, 8192)]);
    }

    [Fact]
    public async Task ShortensARequestThatReachesPastTheEnd()
    {
        var opener = new FakeOpener();
        IBlockSource source = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 8192));

        int read = await source.ReadAsync(4096, new byte[8192], CancellationToken.None);

        read.ShouldBe(4096);
        opener.Device.Reads.ShouldBe([(MiB + 4096, 4096)]);
    }

    [Fact]
    public async Task ReadAtOrPastTheEndReturnsZeroWithoutReading()
    {
        var opener = new FakeOpener();
        IBlockSource source = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 8192));

        int read = await source.ReadAsync(8192, new byte[4096], CancellationToken.None);

        read.ShouldBe(0);
        opener.Device.Reads.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShortReadBeforeTheEndIsASourceReadError()
    {
        var opener = new FakeOpener();
        opener.Device.ReadResult = (_, requested) => requested / 2;
        IBlockSource source = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 4 * MiB));

        var ex = await Should.ThrowAsync<PlatformException>(
            () => source.ReadAsync(0, new byte[4096], CancellationToken.None).AsTask());

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
    }

    [Fact]
    public async Task DeviceIoFailureIsASourceReadError()
    {
        var opener = new FakeOpener();
        opener.Device.ReadResult = (_, _) => throw new IOException("sector unreadable");
        IBlockSource source = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 4 * MiB));

        var ex = await Should.ThrowAsync<PlatformException>(
            () => source.ReadAsync(0, new byte[4096], CancellationToken.None).AsTask());

        ex.Code.ShouldBe(PlatformErrorCode.SourceReadError);
        ex.InnerException.ShouldBeOfType<IOException>();
    }

    [Fact]
    public async Task RejectsAnUnalignedOffset()
    {
        IBlockSource source = new RawPartitionReader(new FakeOpener()).Open(Source, new Extent(MiB, 4 * MiB));

        await Should.ThrowAsync<ArgumentException>(
            () => source.ReadAsync(100, new byte[4096], CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task RejectsABufferThatIsNotASectorMultiple()
    {
        IBlockSource source = new RawPartitionReader(new FakeOpener()).Open(Source, new Extent(MiB, 4 * MiB));

        await Should.ThrowAsync<ArgumentException>(
            () => source.ReadAsync(0, new byte[1000], CancellationToken.None).AsTask());
    }

    [Fact]
    public void RejectsAnExtentThatIsNotSectorAligned()
    {
        var reader = new RawPartitionReader(new FakeOpener());

        Should.Throw<ArgumentException>(() => reader.Open(Source, new Extent(100, Sector)));
        Should.Throw<ArgumentException>(() => reader.Open(Source, new Extent(0, 1000)));
    }

    [Theory]
    [InlineData(1167, PlatformErrorCode.SourceDeviceRemoved)]
    [InlineData(433, PlatformErrorCode.SourceDeviceRemoved)]
    [InlineData(23, PlatformErrorCode.SourceReadError)]
    [InlineData(30, PlatformErrorCode.SourceReadError)]
    [InlineData(121, PlatformErrorCode.SourceReadError)]
    public async Task ReadFailureIsClassifiedByItsWin32Code(int nativeError, PlatformErrorCode expected)
    {
        var opener = new FakeOpener();
        opener.Device.ReadResult = (_, _) => throw new Win32Exception(nativeError);
        IBlockSource source = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 4 * MiB));

        var ex = await Should.ThrowAsync<PlatformException>(
            () => source.ReadAsync(0, new byte[4096], CancellationToken.None).AsTask());

        ex.Code.ShouldBe(expected);
    }

    [Theory]
    [InlineData(5, PlatformErrorCode.AccessDenied)]
    [InlineData(2, PlatformErrorCode.DeviceNotFound)]
    [InlineData(3, PlatformErrorCode.DeviceNotFound)]
    public void OpenFailureIsMappedToAStableCode(int nativeError, PlatformErrorCode expected)
    {
        var opener = new FakeOpener { Failure = new Win32Exception(nativeError) };

        var ex = Should.Throw<PlatformException>(() => new RawPartitionReader(opener).Open(Source, new Extent(MiB, 4 * MiB)));

        ex.Code.ShouldBe(expected);
    }

    [Fact]
    public async Task DisposeClosesTheDeviceAndLaterReadsFail()
    {
        var opener = new FakeOpener();
        IBlockSource source = new RawPartitionReader(opener).Open(Source, new Extent(MiB, 4 * MiB));

        source.Dispose();

        opener.Device.Disposed.ShouldBeTrue();
        await Should.ThrowAsync<ObjectDisposedException>(
            () => source.ReadAsync(0, new byte[4096], CancellationToken.None).AsTask());
    }

    [Fact]
    public void Win32OpenerRefusesAnyWriteRequestBeforeCallingTheOs()
    {
        var request = new RawDeviceOpenRequest(
            @"\\.\PhysicalDrive3",
            Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            Kernel32.OPEN_EXISTING,
            0);

        Should.Throw<InvalidOperationException>(() => new Win32RawDeviceOpener().Open(request));
    }

    private sealed class FakeOpener : IRawDeviceOpener
    {
        public List<RawDeviceOpenRequest> Requests { get; } = [];

        public FakeRawDevice Device { get; } = new();

        public Exception? Failure { get; init; }

        public IRawDevice Open(RawDeviceOpenRequest request)
        {
            Requests.Add(request);

            if (Failure is not null)
            {
                throw Failure;
            }

            return Device;
        }
    }

    private sealed class FakeRawDevice : IRawDevice
    {
        public List<(long Position, int Length)> Reads { get; } = [];

        // Returns the number of bytes "read". The default is a complete read.
        public Func<long, int, int>? ReadResult { get; set; }

        public bool Disposed { get; private set; }

        public int ReadAt(long position, Memory<byte> buffer)
        {
            Reads.Add((position, buffer.Length));

            return ReadResult is null ? buffer.Length : ReadResult(position, buffer.Length);
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
