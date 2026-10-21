using System.Buffers;
using System.ComponentModel;
using System.Runtime.InteropServices;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.RawIo
{
    /// <summary>
    /// Positioned, read-only access to a device. The Win32 implementation is <see cref="Win32RawDevice"/>; tests
    /// replace it with a fake so that no device is ever touched.
    /// </summary>
    internal interface IRawDevice : IDisposable
    {
        /// <summary>
        /// Reads <paramref name="buffer"/>.Length bytes at the absolute device offset <paramref name="position"/>.
        /// Returns the number of bytes read. Throws <see cref="IOException"/> or <see cref="Win32Exception"/> on OS failure.
        /// </summary>
        int ReadAt(long position, Memory<byte> buffer);
    }

    /// <summary>
    /// Read-only block source over one extent of a physical disk, used to copy partitions that are not NTFS (EFI, MSR,
    /// data) as raw bytes (R3, T062).
    /// </summary>
    /// <remarks>
    /// Offsets are relative to the extent. Every read is aligned to <see cref="SectorBytes"/>. The v1 scope covers
    /// 512-byte sector disks (512n and 512e). On 4Kn disks the unbuffered read fails and surfaces as
    /// <see cref="PlatformErrorCode.SourceReadError"/>.
    /// </remarks>
    internal sealed class RawPartitionSource : IBlockSource
    {
        /// <summary>Sector size used for alignment and for the cluster size of the single range.</summary>
        internal const int SectorBytes = 512;

        private readonly IRawDevice _device;
        private readonly long _baseOffset;
        private readonly long _length;
        private bool _disposed;

        internal RawPartitionSource(IRawDevice device, Extent extent)
        {
            ArgumentNullException.ThrowIfNull(device);

            if (extent.Offset % SectorBytes != 0 || extent.Length % SectorBytes != 0)
            {
                throw new ArgumentException("The extent must be aligned to the sector size.", nameof(extent));
            }

            _device = device;
            _baseOffset = extent.Offset;
            _length = extent.Length;
        }

        /// <inheritdoc />
        public ByteSize Length
        {
            get
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return new ByteSize(_length);
            }
        }

        /// <summary>
        /// RawFull: the whole range is one used range. There is no allocation bitmap, so the cluster size is the sector size.
        /// </summary>
        public Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);

            return Task.FromResult(new VolumeAllocation(new ByteSize(SectorBytes), [new Extent(0, _length)]));
        }

        /// <summary>
        /// Reads at <paramref name="offset"/> relative to the extent. A request that reaches past the end of the extent is
        /// shortened to the end. A read that returns fewer bytes than requested, before the end, is a source error.
        /// </summary>
        public ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(_disposed, this);
            ArgumentOutOfRangeException.ThrowIfNegative(offset);

            if (offset % SectorBytes != 0)
            {
                throw new ArgumentException("The offset must be aligned to the sector size.", nameof(offset));
            }

            if (alignedBuffer.Length % SectorBytes != 0)
            {
                throw new ArgumentException("The buffer length must be a multiple of the sector size.", nameof(alignedBuffer));
            }

            if (offset >= _length || alignedBuffer.Length == 0)
            {
                return ValueTask.FromResult(0);
            }

            // Both values are multiples of the sector size, so the shortened request stays aligned.
            int requested = (int)Math.Min(alignedBuffer.Length, _length - offset);

            try
            {
                int read = _device.ReadAt(_baseOffset + offset, alignedBuffer[..requested]);
                if (read < requested)
                {
                    return ValueTask.FromException<int>(new PlatformException(
                        PlatformErrorCode.SourceReadError,
                        $"Short read at offset {offset}: {read} of {requested} bytes."));
                }

                return ValueTask.FromResult(read);
            }
            catch (Exception ex) when (ex is IOException or Win32Exception)
            {
                return ValueTask.FromException<int>(new PlatformException(
                    IoErrorClassifier.ForSourceRead(ex),
                    $"Read at offset {offset} failed.",
                    ex));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _device.Dispose();
        }
    }

    /// <summary>Win32 implementation of <see cref="IRawDevice"/> over a handle opened by <c>CreateFileW</c>.</summary>
    internal sealed class Win32RawDevice(SafeDeviceHandle handle) : IRawDevice
    {
        private readonly SafeDeviceHandle _handle = handle;

        /// <summary>Seeks with SetFilePointerEx, then reads with ReadFile. Requires an unbuffered, sector-aligned call.</summary>
        public unsafe int ReadAt(long position, Memory<byte> buffer)
        {
            if (!Kernel32.SetFilePointerEx(_handle, position, out _, Kernel32.FILE_BEGIN))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "SetFilePointerEx failed.");
            }

            using MemoryHandle pin = buffer.Pin();
            if (!Kernel32.ReadFile(_handle, pin.Pointer, (uint)buffer.Length, out uint read, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "ReadFile failed.");
            }

            return (int)read;
        }

        public void Dispose() => _handle.Dispose();
    }
}

namespace WinCopy.Infrastructure.Windows.Interop
{
    // Declared in the RawIo folder so this change touches only the RawIo files. The members are plain LibraryImport
    // declarations and can move to Interop/Kernel32.ReadWrite.cs later without changing their signatures.
    internal static partial class Kernel32
    {
        internal const uint FILE_BEGIN = 0;

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool SetFilePointerEx(
            SafeDeviceHandle hFile,
            long liDistanceToMove,
            out long lpNewFilePointer,
            uint dwMoveMethod);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool ReadFile(
            SafeDeviceHandle hFile,
            void* lpBuffer,
            uint nNumberOfBytesToRead,
            out uint lpNumberOfBytesRead,
            IntPtr lpOverlapped);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool WriteFile(
            SafeDeviceHandle hFile,
            void* lpBuffer,
            uint nNumberOfBytesToWrite,
            out uint lpNumberOfBytesWritten,
            IntPtr lpOverlapped);
    }
}
