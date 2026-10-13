using System.Buffers;
using System.Runtime.InteropServices;
using System.Threading.Channels;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Native memory block aligned to a given boundary, suitable for unbuffered I/O.
/// Allocated with <see cref="NativeMemory.AlignedAlloc"/> and freed on <see cref="Dispose"/>.
/// </summary>
internal sealed unsafe class AlignedBuffer : IDisposable
{
    private const nuint MinimumAlignment = 512;

    private readonly int _length;
    private readonly nuint _alignment;
    private readonly NativeBufferMemoryManager _memoryManager;
    private byte* _pointer;
    private bool _disposed;

    /// <param name="length">Size in bytes. Must be positive and a multiple of <paramref name="alignment"/>.</param>
    /// <param name="alignment">Power of two, at least 512.</param>
    public AlignedBuffer(int length, nuint alignment)
    {
        if (length <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length), length, "Length must be positive.");
        }

        if (alignment < MinimumAlignment || (alignment & (alignment - 1)) != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(alignment), alignment, "Alignment must be a power of two and at least 512.");
        }

        if ((nuint)length % alignment != 0)
        {
            throw new ArgumentException("Length must be a multiple of the alignment.", nameof(length));
        }

        _length = length;
        _alignment = alignment;
        _pointer = (byte*)NativeMemory.AlignedAlloc((nuint)length, alignment);
        _memoryManager = new NativeBufferMemoryManager(this);
    }

    public int Length => _length;

    public nuint Alignment => _alignment;

    /// <summary>Address of the first byte. Used by tests to verify alignment.</summary>
    internal nuint Address
    {
        get
        {
            ThrowIfDisposed();
            return (nuint)_pointer;
        }
    }

    public Span<byte> Span
    {
        get
        {
            ThrowIfDisposed();
            return new Span<byte>(_pointer, _length);
        }
    }

    public Memory<byte> Memory
    {
        get
        {
            ThrowIfDisposed();
            return _memoryManager.Memory;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        NativeMemory.AlignedFree(_pointer);
        _pointer = null;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    /// <summary>Exposes the native block as <see cref="Memory{T}"/>. Native memory is already pinned.</summary>
    private sealed class NativeBufferMemoryManager : MemoryManager<byte>
    {
        private readonly AlignedBuffer _owner;

        public NativeBufferMemoryManager(AlignedBuffer owner) => _owner = owner;

        public override Span<byte> GetSpan() => _owner.Span;

        public override MemoryHandle Pin(int elementIndex = 0)
        {
            Span<byte> span = _owner.Span;
            if ((uint)elementIndex > (uint)span.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(elementIndex));
            }

            return new MemoryHandle(_owner._pointer + elementIndex);
        }

        public override void Unpin()
        {
        }

        protected override void Dispose(bool disposing)
        {
        }
    }
}

/// <summary>
/// Fixed set of <see cref="AlignedBuffer"/> instances. <see cref="RentAsync"/> waits
/// asynchronously while the pool is empty; <see cref="Return"/> makes a buffer available again.
/// </summary>
internal sealed class AlignedBufferPool : IDisposable
{
    public const int DefaultBufferCount = 4;
    public const int DefaultBufferSize = 4 * 1024 * 1024;

    private readonly AlignedBuffer[] _buffers;
    private readonly bool[] _rented;
    private readonly Channel<AlignedBuffer> _available;
    private readonly object _gate = new();
    private bool _disposed;

    public AlignedBufferPool(nuint alignment, int bufferCount = DefaultBufferCount, int bufferSize = DefaultBufferSize)
    {
        if (bufferCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bufferCount), bufferCount, "Buffer count must be positive.");
        }

        _buffers = new AlignedBuffer[bufferCount];
        _rented = new bool[bufferCount];
        _available = Channel.CreateBounded<AlignedBuffer>(bufferCount);

        try
        {
            for (int i = 0; i < bufferCount; i++)
            {
                _buffers[i] = new AlignedBuffer(bufferSize, alignment);
                _available.Writer.TryWrite(_buffers[i]);
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Waits until a buffer is available and returns it.</summary>
    public async ValueTask<AlignedBuffer> RentAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        AlignedBuffer buffer;
        try
        {
            buffer = await _available.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ChannelClosedException) when (_disposed)
        {
            throw new ObjectDisposedException(nameof(AlignedBufferPool));
        }

        lock (_gate)
        {
            _rented[Array.IndexOf(_buffers, buffer)] = true;
        }

        return buffer;
    }

    /// <summary>Returns a rented buffer to the pool. Only buffers currently rented from this pool are accepted.</summary>
    public void Return(AlignedBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ObjectDisposedException.ThrowIf(_disposed, this);

        int index = Array.IndexOf(_buffers, buffer);
        if (index < 0)
        {
            throw new ArgumentException("The buffer does not belong to this pool.", nameof(buffer));
        }

        lock (_gate)
        {
            if (!_rented[index])
            {
                throw new InvalidOperationException("The buffer is not rented from this pool.");
            }

            _rented[index] = false;
        }

        _available.Writer.TryWrite(buffer);
    }

    /// <summary>Frees every buffer owned by the pool, including any still rented out.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _available.Writer.TryComplete();

        foreach (AlignedBuffer? buffer in _buffers)
        {
            buffer?.Dispose();
        }
    }
}
