using Shouldly;
using WinClonerX.Infrastructure.Windows.RawIo;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

public sealed class AlignedBufferTests
{
    private const int SmallBufferSize = 4096;

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    public void AddressIsAlignedToRequestedBoundary(int alignment)
    {
        using var buffer = new AlignedBuffer(alignment * 2, (nuint)alignment);

        (buffer.Address % (nuint)alignment).ShouldBe((nuint)0);
        buffer.Length.ShouldBe(alignment * 2);
        buffer.Alignment.ShouldBe((nuint)alignment);
    }

    [Fact]
    public void SpanHasRequestedLengthAndIsWritable()
    {
        using var buffer = new AlignedBuffer(SmallBufferSize, 512);

        buffer.Span.Length.ShouldBe(SmallBufferSize);
        buffer.Span[0] = 0xAB;
        buffer.Span[^1] = 0xCD;

        buffer.Span[0].ShouldBe((byte)0xAB);
        buffer.Span[^1].ShouldBe((byte)0xCD);
    }

    [Fact]
    public void MemorySharesStorageWithSpan()
    {
        using var buffer = new AlignedBuffer(SmallBufferSize, 512);

        buffer.Memory.Length.ShouldBe(SmallBufferSize);
        buffer.Memory.Span[10] = 0x5A;

        buffer.Span[10].ShouldBe((byte)0x5A);
    }

    [Fact]
    public void MemoryCanBePinnedWhileBufferIsAligned()
    {
        using var buffer = new AlignedBuffer(SmallBufferSize, 4096);

        using (buffer.Memory.Pin())
        {
            (buffer.Address % 4096).ShouldBe((nuint)0);
        }
    }

    [Theory]
    [InlineData(0, 512)]
    [InlineData(-512, 512)]
    public void ConstructorRejectsNonPositiveSize(int size, int alignment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlignedBuffer(size, (nuint)alignment));
    }

    [Theory]
    [InlineData(512, 0)]
    [InlineData(512, 256)]
    [InlineData(512, 768)]
    [InlineData(512, 1000)]
    public void ConstructorRejectsInvalidAlignment(int size, int alignment)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlignedBuffer(size, (nuint)alignment));
    }

    [Theory]
    [InlineData(1000, 512)]
    [InlineData(4095, 4096)]
    public void ConstructorRejectsSizeNotMultipleOfAlignment(int size, int alignment)
    {
        Assert.Throws<ArgumentException>(() => new AlignedBuffer(size, (nuint)alignment));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var buffer = new AlignedBuffer(SmallBufferSize, 512);

        buffer.Dispose();
        buffer.Dispose();
    }

    [Fact]
    public void SpanThrowsAfterDispose()
    {
        var buffer = new AlignedBuffer(SmallBufferSize, 512);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = buffer.Span; });
    }

    [Fact]
    public void MemoryThrowsAfterDispose()
    {
        var buffer = new AlignedBuffer(SmallBufferSize, 512);
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = buffer.Memory; });
    }

    [Fact]
    public void MemoryObtainedBeforeDisposeThrowsWhenAccessedAfterDispose()
    {
        var buffer = new AlignedBuffer(SmallBufferSize, 512);
        Memory<byte> memory = buffer.Memory;
        buffer.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = memory.Span; });
    }
}

public sealed class AlignedBufferPoolTests
{
    private const int SmallBufferSize = 4096;

    [Fact]
    public async Task RentAsyncReturnsBuffersWithRequestedAlignment()
    {
        using var pool = new AlignedBufferPool(4096, bufferCount: 2, bufferSize: SmallBufferSize);

        AlignedBuffer buffer = await pool.RentAsync(CancellationToken.None);

        buffer.Length.ShouldBe(SmallBufferSize);
        (buffer.Address % 4096).ShouldBe((nuint)0);
        pool.Return(buffer);
    }

    [Fact]
    public void DefaultsAreFourBuffersOfFourMebibytes()
    {
        AlignedBufferPool.DefaultBufferSize.ShouldBe(4 * 1024 * 1024);
        AlignedBufferPool.DefaultBufferCount.ShouldBe(4);
    }

    [Fact]
    public async Task RentAsyncBlocksWhenEmptyAndResumesWhenBufferReturned()
    {
        using var pool = new AlignedBufferPool(512, bufferCount: 1, bufferSize: 512);

        AlignedBuffer held = await pool.RentAsync(CancellationToken.None);
        Task<AlignedBuffer> waiting = pool.RentAsync(CancellationToken.None).AsTask();

        await Task.Delay(100, TestContext.Current.CancellationToken);
        waiting.IsCompleted.ShouldBeFalse();

        pool.Return(held);

        AlignedBuffer resumed = await waiting.WaitAsync(
            TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        resumed.ShouldBeSameAs(held);
        pool.Return(resumed);
    }

    [Fact]
    public async Task RentAsyncHonorsCancellationWhileWaiting()
    {
        using var pool = new AlignedBufferPool(512, bufferCount: 1, bufferSize: 512);
        AlignedBuffer held = await pool.RentAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        Task<AlignedBuffer> waiting = pool.RentAsync(cts.Token).AsTask();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);

        // A cancelled waiter must not consume the buffer: returning it makes it rentable again.
        pool.Return(held);
        AlignedBuffer again = await pool.RentAsync(CancellationToken.None);
        again.ShouldBeSameAs(held);
        pool.Return(again);
    }

    [Fact]
    public async Task RentAsyncThrowsWhenAlreadyCancelledAndEmpty()
    {
        using var pool = new AlignedBufferPool(512, bufferCount: 1, bufferSize: 512);
        AlignedBuffer held = await pool.RentAsync(CancellationToken.None);

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pool.RentAsync(cts.Token).AsTask());

        pool.Return(held);
    }

    [Fact]
    public void ReturnRejectsBufferFromAnotherPool()
    {
        using var pool = new AlignedBufferPool(512, bufferCount: 1, bufferSize: 512);
        using var foreign = new AlignedBuffer(512, 512);

        Assert.Throws<ArgumentException>(() => pool.Return(foreign));
    }

    [Fact]
    public async Task ReturnRejectsReturnOfBufferThatIsNotRented()
    {
        using var pool = new AlignedBufferPool(512, bufferCount: 2, bufferSize: 512);
        AlignedBuffer first = await pool.RentAsync(CancellationToken.None);
        pool.Return(first);

        AlignedBuffer second = await pool.RentAsync(CancellationToken.None);

        // "first" is available again, so returning it a second time must be rejected even though the
        // queue has room for it.
        Assert.Throws<InvalidOperationException>(() => pool.Return(first));

        pool.Return(second);
    }

    [Fact]
    public async Task RentAsyncThrowsAfterDispose()
    {
        var pool = new AlignedBufferPool(512, bufferCount: 1, bufferSize: 512);
        pool.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => pool.RentAsync(CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task DisposeFreesBuffersAndIsIdempotent()
    {
        var pool = new AlignedBufferPool(512, bufferCount: 2, bufferSize: 512);
        AlignedBuffer buffer = await pool.RentAsync(CancellationToken.None);

        pool.Dispose();
        pool.Dispose();

        Assert.Throws<ObjectDisposedException>(() => { _ = buffer.Span; });
    }

    [Fact]
    public void ConstructorRejectsNonPositiveBufferCount()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new AlignedBufferPool(512, bufferCount: 0, bufferSize: 512));
    }
}
