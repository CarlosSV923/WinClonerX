using System.ComponentModel;
using System.Runtime.InteropServices;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Interop;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// The device operations that <see cref="SnapshotVolumeSource"/> needs from a VSS snapshot volume. The Win32
/// implementation is <see cref="Win32SnapshotDevice"/>; tests replace it with a fake so that no device is opened.
/// Every operation is read-only.
/// </summary>
internal interface ISnapshotDevice : IDisposable
{
    /// <summary>FSCTL_ALLOW_EXTENDED_DASD_IO. Without it the tail and the backup boot sector cannot be read (research R2).</summary>
    void AllowExtendedDasdIo();

    /// <summary>IOCTL_DISK_GET_LENGTH_INFO: the length of the device in bytes.</summary>
    long GetLength();

    /// <summary>FSCTL_GET_NTFS_VOLUME_DATA: the geometry of the NTFS volume in the snapshot.</summary>
    NTFS_VOLUME_DATA_BUFFER GetNtfsVolumeData();

    /// <summary>
    /// FSCTL_GET_VOLUME_BITMAP from <paramref name="startingLcn"/>. The bitmap bits are written to the start of
    /// <paramref name="output"/> without the header, least significant bit first. Returns the number of bytes written.
    /// <paramref name="chunkStartingLcn"/> is the first cluster of the chunk. The device may round it down to a multiple of
    /// 8, so it can be below <paramref name="startingLcn"/>.
    /// <paramref name="chunkClusters"/> is the number of clusters this chunk covers, from <paramref name="chunkStartingLcn"/>.
    /// It is not the BitmapSize of the header, which counts the clusters from the chunk to the END OF THE VOLUME. The next
    /// request starts at <c>chunkStartingLcn + chunkClusters</c>.
    /// <paramref name="moreData"/> is true when the output was full and more of the bitmap remains.
    /// </summary>
    int ReadBitmapChunk(
        long startingLcn, Span<byte> output, out long chunkStartingLcn, out long chunkClusters, out bool moreData);

    /// <summary>Reads at the absolute device offset. Returns fewer bytes than requested only when the device ends.</summary>
    int Read(long offset, Span<byte> alignedDestination);
}

/// <summary>
/// Win32 implementation of <see cref="ISnapshotDevice"/>. The device is opened with GENERIC_READ only, so no member
/// of this class can write to the snapshot.
/// </summary>
internal sealed unsafe class Win32SnapshotDevice : ISnapshotDevice
{
    private const int ErrorMoreData = 234;

    private readonly SafeDeviceHandle _handle;

    /// <summary>Opens a snapshot device path, for example <c>\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN</c>.</summary>
    /// <exception cref="Win32Exception">The OS refuses the open.</exception>
    public Win32SnapshotDevice(string devicePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(devicePath);

        SafeDeviceHandle handle = Kernel32.CreateFileW(
            devicePath,
            Kernel32.GENERIC_READ,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            Kernel32.FILE_FLAG_NO_BUFFERING,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open the snapshot device {devicePath} for reading.");
        }

        _handle = handle;
    }

    /// <inheritdoc />
    public void AllowExtendedDasdIo()
    {
        if (!Kernel32.DeviceIoControl(_handle, IoControlCodes.FSCTL_ALLOW_EXTENDED_DASD_IO, null, 0, null, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "FSCTL_ALLOW_EXTENDED_DASD_IO failed.");
        }
    }

    /// <inheritdoc />
    public long GetLength()
    {
        GET_LENGTH_INFORMATION info = default;
        if (!Kernel32.DeviceIoControl(
                _handle,
                IoControlCodes.IOCTL_DISK_GET_LENGTH_INFO,
                null,
                0,
                &info,
                (uint)sizeof(GET_LENGTH_INFORMATION),
                out uint returned,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "IOCTL_DISK_GET_LENGTH_INFO failed.");
        }

        if (returned < (uint)sizeof(GET_LENGTH_INFORMATION))
        {
            throw new InvalidOperationException($"IOCTL_DISK_GET_LENGTH_INFO returned only {returned} bytes.");
        }

        return info.Length;
    }

    /// <inheritdoc />
    public NTFS_VOLUME_DATA_BUFFER GetNtfsVolumeData()
    {
        NTFS_VOLUME_DATA_BUFFER data = default;
        if (!Kernel32.DeviceIoControl(
                _handle,
                IoControlCodes.FSCTL_GET_NTFS_VOLUME_DATA,
                null,
                0,
                &data,
                (uint)sizeof(NTFS_VOLUME_DATA_BUFFER),
                out uint returned,
                IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "FSCTL_GET_NTFS_VOLUME_DATA failed.");
        }

        if (returned < (uint)sizeof(NTFS_VOLUME_DATA_BUFFER))
        {
            throw new InvalidOperationException($"FSCTL_GET_NTFS_VOLUME_DATA returned only {returned} bytes.");
        }

        return data;
    }

    /// <inheritdoc />
    /// <remarks>
    /// ERROR_MORE_DATA (234) means the output buffer is full, so it is success with <paramref name="moreData"/> true. The
    /// header and the byte count are checked by <see cref="VolumeBitmapParser"/>. BitmapSize there counts the clusters to the
    /// end of the volume, not the clusters in this buffer.
    /// </remarks>
    public int ReadBitmapChunk(
        long startingLcn, Span<byte> output, out long chunkStartingLcn, out long chunkClusters, out bool moreData)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(startingLcn);
        if (output.Length <= VolumeBitmapParser.HeaderBytes)
        {
            throw new ArgumentException("The output buffer is too small for a volume bitmap.", nameof(output));
        }

        var input = new STARTING_LCN_INPUT_BUFFER { StartingLcn = startingLcn };
        bool ok;
        int error = 0;
        uint returned = 0;

        fixed (byte* outputPointer = output)
        {
            ok = Kernel32.DeviceIoControl(
                _handle,
                IoControlCodes.FSCTL_GET_VOLUME_BITMAP,
                &input,
                (uint)sizeof(STARTING_LCN_INPUT_BUFFER),
                outputPointer,
                (uint)output.Length,
                out returned,
                IntPtr.Zero);

            if (!ok)
            {
                error = Marshal.GetLastPInvokeError();
            }
        }

        if (!ok && error != ErrorMoreData)
        {
            throw new Win32Exception(error, "FSCTL_GET_VOLUME_BITMAP failed.");
        }

        moreData = !ok;
        return VolumeBitmapParser.Parse(output, returned, moreData, out chunkStartingLcn, out chunkClusters);
    }

    /// <inheritdoc />
    public int Read(long offset, Span<byte> alignedDestination)
    {
        if (!Kernel32.SetFilePointerEx(_handle, offset, out _, Kernel32.FILE_BEGIN))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"SetFilePointerEx to offset {offset} failed.");
        }

        int done = 0;
        fixed (byte* pointer = alignedDestination)
        {
            while (done < alignedDestination.Length)
            {
                if (!Kernel32.ReadFile(_handle, pointer + done, (uint)(alignedDestination.Length - done), out uint read, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"ReadFile at offset {offset} failed.");
                }

                if (read == 0)
                {
                    break;
                }

                done += (int)read;
            }
        }

        return done;
    }

    /// <inheritdoc />
    public void Dispose() => _handle.Dispose();
}

/// <summary>
/// Read-only block source over the VSS snapshot of one NTFS volume (T061, research R2). The allocation comes from the
/// volume bitmap of the snapshot, so the copy takes only the used clusters plus the boot region, the tail and the NTFS
/// backup boot sector. Reads are aligned to the volume's sector size.
/// </summary>
internal sealed class SnapshotVolumeSource : IBlockSource
{
    /// <summary>Size of the output buffer for each FSCTL_GET_VOLUME_BITMAP call, header included.</summary>
    internal const int BitmapChunkBytes = 1024 * 1024;

    private readonly ISnapshotDevice _device;
    private readonly long _length;
    private readonly NTFS_VOLUME_DATA_BUFFER _volume;
    private readonly int _bitmapChunkBytes;
    private VolumeAllocation? _allocation;
    private bool _disposed;

    /// <summary>
    /// Takes ownership of <paramref name="device"/> and queries its geometry. FSCTL_ALLOW_EXTENDED_DASD_IO is the first
    /// call, because without it the tail and the backup boot sector cannot be read. The device is disposed when the
    /// constructor fails.
    /// </summary>
    /// <exception cref="PlatformException">The device refuses the geometry queries.</exception>
    internal SnapshotVolumeSource(ISnapshotDevice device)
        : this(device, BitmapChunkBytes)
    {
    }

    /// <summary>
    /// As <see cref="SnapshotVolumeSource(ISnapshotDevice)"/>, with the output buffer size of each bitmap call given.
    /// Tests pass a small size to make a small volume need several chunks.
    /// </summary>
    /// <param name="device">The device. It is disposed when the constructor fails.</param>
    /// <param name="bitmapChunkBytes">The output buffer size of each bitmap call, header included. It must be larger than the header.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="bitmapChunkBytes"/> is not larger than the bitmap header.</exception>
    /// <exception cref="PlatformException">The device refuses the geometry queries.</exception>
    internal SnapshotVolumeSource(ISnapshotDevice device, int bitmapChunkBytes)
    {
        ArgumentNullException.ThrowIfNull(device);

        long length;
        NTFS_VOLUME_DATA_BUFFER volume;
        try
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(bitmapChunkBytes, VolumeBitmapParser.HeaderBytes);

            device.AllowExtendedDasdIo();
            length = device.GetLength();
            volume = device.GetNtfsVolumeData();

            if (volume.BytesPerSector == 0 || volume.BytesPerCluster == 0 || volume.TotalClusters <= 0)
            {
                throw new InvalidOperationException("The snapshot volume reports no usable NTFS geometry.");
            }
        }
        catch (Win32Exception ex)
        {
            device.Dispose();
            throw new PlatformException(
                PlatformErrorCode.SourceReadError, "Could not read the geometry of the snapshot volume.", ex);
        }
        catch (Exception)
        {
            device.Dispose();
            throw;
        }

        _device = device;
        _length = length;
        _volume = volume;
        _bitmapChunkBytes = bitmapChunkBytes;
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
    /// Reads the volume bitmap from cluster 0 to the end and plans the ranges to copy. The result is cached, so the device
    /// is queried only once.
    /// </summary>
    public async Task<VolumeAllocation> GetAllocationAsync(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_allocation is null)
        {
            _allocation = await Task.Run(() => BuildAllocation(ct), ct).ConfigureAwait(false);
        }

        return _allocation;
    }

    /// <summary>
    /// Reads from the snapshot at <paramref name="offset"/>. Offset and length must be multiples of the sector size, and the
    /// read must end at or before <see cref="Length"/>. A short read or a device failure is a source read error.
    /// </summary>
    /// <exception cref="ArgumentException">The offset or the length is not a multiple of the sector size.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The read ends beyond <see cref="Length"/>.</exception>
    public ValueTask<int> ReadAsync(long offset, Memory<byte> alignedBuffer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(offset);

        int sectorBytes = (int)_volume.BytesPerSector;
        if (offset % sectorBytes != 0)
        {
            throw new ArgumentException($"The offset must be a multiple of the sector size {sectorBytes}.", nameof(offset));
        }

        if (alignedBuffer.Length % sectorBytes != 0)
        {
            throw new ArgumentException(
                $"The buffer length must be a multiple of the sector size {sectorBytes}.", nameof(alignedBuffer));
        }

        if (offset > _length || alignedBuffer.Length > _length - offset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset), offset, "The read ends beyond the length of the snapshot volume.");
        }

        if (alignedBuffer.Length == 0)
        {
            return ValueTask.FromResult(0);
        }

        try
        {
            int read = _device.Read(offset, alignedBuffer.Span);
            if (read < alignedBuffer.Length)
            {
                return ValueTask.FromException<int>(new PlatformException(
                    PlatformErrorCode.SourceReadError,
                    $"Short read at offset {offset}: {read} of {alignedBuffer.Length} bytes."));
            }

            return ValueTask.FromResult(read);
        }
        catch (Exception ex) when (ex is IOException or Win32Exception)
        {
            return ValueTask.FromException<int>(new PlatformException(
                PlatformErrorCode.SourceReadError,
                $"Read at offset {offset} failed.",
                ex));
        }
    }

    /// <summary>Closes the snapshot device. Calling it again has no effect.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _device.Dispose();
    }

    private VolumeAllocation BuildAllocation(CancellationToken ct)
    {
        List<(long StartLcn, long Count)> runs;
        try
        {
            runs = ReadUsedRuns(ct);
        }
        catch (Win32Exception ex)
        {
            throw new PlatformException(
                PlatformErrorCode.SourceReadError, "Could not read the volume bitmap of the snapshot.", ex);
        }

        return VolumeAllocationPlanner.Plan(
            runs,
            _volume.BytesPerCluster,
            _volume.TotalClusters,
            _volume.NumberSectors,
            _volume.BytesPerSector,
            _length);
    }

    /// <summary>
    /// Walks the volume bitmap from cluster 0 and returns the runs of used clusters. Each call covers the clusters from the
    /// returned start up to the chunk's cluster count, and the next call starts where that chunk ends. Runs that cross a
    /// chunk boundary are merged. The walk ends when the bitmap reaches the last cluster. A bitmap that stops early is an
    /// error, because the clusters that are missing would never be copied.
    /// </summary>
    private List<(long StartLcn, long Count)> ReadUsedRuns(CancellationToken ct)
    {
        long totalClusters = _volume.TotalClusters;
        var runs = new List<(long StartLcn, long Count)>();
        byte[] chunk = new byte[_bitmapChunkBytes];
        long nextLcn = 0;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            long requested = nextLcn;
            int bytes = _device.ReadBitmapChunk(requested, chunk, out long chunkStart, out long chunkClusters, out bool moreData);

            // The device may start the bitmap before the requested cluster, so the clusters below it are skipped. The chunk
            // must reach past the request, or the walk would not advance.
            if (chunkStart > requested || chunkClusters <= 0 || chunkStart + chunkClusters <= requested ||
                bytes < (chunkClusters + 7) / 8 || bytes > chunk.Length)
            {
                throw new InvalidOperationException(
                    $"Unexpected volume bitmap at cluster {requested}: it starts at {chunkStart} and covers {chunkClusters} clusters.");
            }

            // A chunk with more data after it must end on a byte boundary, or the next chunk would not line up with its bits.
            if (moreData && chunkClusters % 8 != 0)
            {
                throw new InvalidOperationException(
                    $"The volume bitmap chunk at cluster {chunkStart} covers {chunkClusters} clusters, not a whole number of bytes.");
            }

            long chunkEnd = Math.Min(chunkStart + chunkClusters, totalClusters);
            IReadOnlyList<(long StartLcn, long Count)> chunkRuns =
                VolumeAllocationPlanner.UsedRuns(chunk.AsSpan(0, bytes), chunkStart, totalClusters);
            AppendClipped(runs, chunkRuns, requested, chunkEnd);

            nextLcn = chunkStart + chunkClusters;
            if (nextLcn >= totalClusters)
            {
                break;
            }

            if (!moreData)
            {
                throw new InvalidOperationException(
                    $"The volume bitmap ended at cluster {nextLcn} of {totalClusters}.");
            }
        }

        return runs;
    }

    /// <summary>
    /// Adds the part of each chunk run that lies in [<paramref name="from"/>, <paramref name="to"/>). A run that touches the
    /// previous run is merged with it.
    /// </summary>
    private static void AppendClipped(
        List<(long StartLcn, long Count)> runs,
        IReadOnlyList<(long StartLcn, long Count)> chunkRuns,
        long from,
        long to)
    {
        foreach ((long startLcn, long count) in chunkRuns)
        {
            long start = Math.Max(startLcn, from);
            long end = Math.Min(startLcn + count, to);
            if (start >= end)
            {
                continue;
            }

            if (runs.Count > 0 && runs[^1].StartLcn + runs[^1].Count == start)
            {
                runs[^1] = (runs[^1].StartLcn, runs[^1].Count + (end - start));
            }
            else
            {
                runs.Add((start, end - start));
            }
        }
    }
}
