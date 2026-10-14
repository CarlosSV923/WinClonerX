using System.ComponentModel;
using System.Runtime.InteropServices;
using WinCopy.Infrastructure.Windows.Interop;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.IntegrationTests.Spikes;

/// <summary>A run of consecutive used clusters, in NTFS logical cluster numbers (LCN).</summary>
internal readonly record struct ClusterRun(long StartLcn, long ClusterCount);

/// <summary>
/// Where the tail and the NTFS backup boot sector go on the target partition. <see cref="Plan"/> is pure: it does no I/O.
/// All values are byte offsets relative to the start of the volume.
/// </summary>
/// <param name="TailOffset">First byte after the last full cluster (<c>TotalClusters * BytesPerCluster</c>).</param>
/// <param name="TailLength">Bytes to copy from the tail, rounded down to whole sectors. Zero when there is no tail to copy.</param>
/// <param name="BackupOffset">Offset of the NTFS backup boot sector: <c>NumberSectors * BytesPerSector</c>.</param>
/// <param name="BackupFromSource">
/// True when the snapshot holds the backup sector and it is read from there. False when it must be synthesized from the
/// primary boot sector.
/// </param>
internal readonly record struct NtfsTailPlan(long TailOffset, long TailLength, long BackupOffset, bool BackupFromSource)
{
    /// <summary>Size of the NTFS boot sector, which is also the size of the backup copy.</summary>
    private const long BackupBytes = SpikeVolumeCopy.SectorBytes;

    /// <summary>
    /// Computes the tail and backup placement. The NTFS backup boot sector is not in the last sector of the volume as
    /// <c>NumberSectors - 1</c>; it is the sector right after the volume, at byte <c>NumberSectors * BytesPerSector</c>,
    /// which is the last sector of the partition.
    /// </summary>
    /// <exception cref="InvalidOperationException">The backup boot sector would end beyond the target partition.</exception>
    public static NtfsTailPlan Plan(
        long totalClusters,
        long bytesPerCluster,
        long numberSectors,
        long bytesPerSector,
        long sourceLength,
        long targetPartitionLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(totalClusters);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerCluster);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(numberSectors);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bytesPerSector);
        ArgumentOutOfRangeException.ThrowIfNegative(sourceLength);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(targetPartitionLength);

        long tailOffset = checked(totalClusters * bytesPerCluster);
        long volumeBytes = checked(numberSectors * bytesPerSector);
        long backupOffset = volumeBytes;
        long backupEnd = checked(backupOffset + BackupBytes);

        if (backupEnd > targetPartitionLength)
        {
            throw new InvalidOperationException(
                $"The NTFS backup boot sector would end at byte {backupEnd}, beyond the target partition " +
                $"({targetPartitionLength} bytes). The target is too small for this volume.");
        }

        // The tail runs up to the end of the volume, but never past the end of the snapshot device.
        long usableEnd = Math.Min(volumeBytes, sourceLength);
        usableEnd -= usableEnd % bytesPerSector;

        long tailLength = Math.Max(0, usableEnd - tailOffset);
        tailLength -= tailLength % bytesPerSector;

        bool backupFromSource = backupEnd <= sourceLength;

        return new NtfsTailPlan(tailOffset, tailLength, backupOffset, backupFromSource);
    }
}

/// <summary>Spike-local ReadFile, WriteFile and SetFilePointerEx. Only the spike uses them.</summary>
internal static unsafe partial class SpikeWin32
{
    public const uint FileBegin = 0;
    public const uint ErrorMoreData = 234;

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetFilePointerEx(SafeHandle handle, long distance, IntPtr newPointer, uint moveMethod);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(
        SafeHandle handle, byte* buffer, uint bytesToRead, out uint bytesRead, IntPtr overlapped);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(
        SafeHandle handle, byte* buffer, uint bytesToWrite, out uint bytesWritten, IntPtr overlapped);
}

/// <summary>
/// Reads an NTFS volume from a snapshot device and copies its used clusters to a partition of another disk.
/// Every offset and length is a multiple of 512 bytes, and every buffer is 4096-byte aligned, so unbuffered I/O works.
/// </summary>
internal static class SpikeVolumeCopy
{
    /// <summary>Logical sector size of the VHDX fixtures (<see cref="VirtualDiskFixture"/> uses 512).</summary>
    public const int SectorBytes = 512;

    public const int AlignmentBytes = 4096;
    public const int CopyChunkBytes = 1024 * 1024;
    public const int BitmapBufferBytes = 64 * 1024;

    /// <summary>Size of VOLUME_BITMAP_BUFFER, the header in front of the bitmap bits.</summary>
    private const int BitmapHeaderBytes = 16;

    private const uint GenericWrite = 0x40000000;

    /// <summary>Opens a device or volume path with the given access and CreateFile flags, sharing read and write.</summary>
    public static SafeDeviceHandle OpenDevice(string path, uint access, uint flags)
    {
        SafeDeviceHandle handle = Kernel32.CreateFileW(
            path,
            access,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            flags,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open '{path}'.");
        }

        return handle;
    }

    /// <summary>FSCTL_GET_NTFS_VOLUME_DATA. Fails when the volume is not mounted as NTFS.</summary>
    public static unsafe NTFS_VOLUME_DATA_BUFFER ReadNtfsVolumeData(SafeDeviceHandle volume)
    {
        NTFS_VOLUME_DATA_BUFFER data = default;
        bool ok = Kernel32.DeviceIoControl(
            volume,
            IoControlCodes.FSCTL_GET_NTFS_VOLUME_DATA,
            null,
            0,
            &data,
            (uint)sizeof(NTFS_VOLUME_DATA_BUFFER),
            out uint returned,
            IntPtr.Zero);

        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "FSCTL_GET_NTFS_VOLUME_DATA failed.");
        }

        if (returned < (uint)sizeof(NTFS_VOLUME_DATA_BUFFER))
        {
            throw new InvalidOperationException($"FSCTL_GET_NTFS_VOLUME_DATA returned only {returned} bytes.");
        }

        return data;
    }

    /// <summary>
    /// Walks FSCTL_GET_VOLUME_BITMAP from cluster 0 to the end, following ERROR_MORE_DATA, and returns the runs of used clusters.
    /// </summary>
    public static unsafe List<ClusterRun> ReadUsedRuns(SafeDeviceHandle volume, long totalClusters, out long usedClusters)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(totalClusters);

        var runs = new List<ClusterRun>();
        byte[] output = new byte[BitmapBufferBytes];
        long nextLcn = 0;
        long runStart = -1;
        usedClusters = 0;

        while (true)
        {
            long requestedLcn = nextLcn;
            var input = new STARTING_LCN_INPUT_BUFFER { StartingLcn = requestedLcn };
            bool ok;
            int error = 0;

            fixed (byte* outputPointer = output)
            {
                ok = Kernel32.DeviceIoControl(
                    volume,
                    IoControlCodes.FSCTL_GET_VOLUME_BITMAP,
                    &input,
                    (uint)sizeof(STARTING_LCN_INPUT_BUFFER),
                    outputPointer,
                    (uint)output.Length,
                    out _,
                    IntPtr.Zero);

                if (!ok)
                {
                    error = Marshal.GetLastPInvokeError();
                }
            }

            if (!ok && error != SpikeWin32.ErrorMoreData)
            {
                throw new Win32Exception(error, "FSCTL_GET_VOLUME_BITMAP failed.");
            }

            VOLUME_BITMAP_BUFFER header = MemoryMarshal.Read<VOLUME_BITMAP_BUFFER>(output);
            long bitmapBytes = (header.BitmapSize + 7) / 8;
            if (header.StartingLcn > requestedLcn || header.BitmapSize <= 0 || BitmapHeaderBytes + bitmapBytes > output.Length)
            {
                throw new InvalidOperationException($"Unexpected volume bitmap header at cluster {requestedLcn}.");
            }

            for (long bit = 0; bit < header.BitmapSize; bit++)
            {
                long lcn = header.StartingLcn + bit;
                if (lcn < requestedLcn)
                {
                    continue;
                }

                if (lcn >= totalClusters)
                {
                    break;
                }

                bool used = (output[BitmapHeaderBytes + (int)(bit >> 3)] & (1 << (int)(bit & 7))) != 0;
                if (used)
                {
                    usedClusters++;
                    if (runStart < 0)
                    {
                        runStart = lcn;
                    }
                }
                else if (runStart >= 0)
                {
                    runs.Add(new ClusterRun(runStart, lcn - runStart));
                    runStart = -1;
                }
            }

            nextLcn = header.StartingLcn + header.BitmapSize;
            if (nextLcn >= totalClusters)
            {
                break;
            }

            if (ok)
            {
                throw new InvalidOperationException($"The volume bitmap ended at cluster {nextLcn} of {totalClusters}.");
            }
        }

        if (runStart >= 0)
        {
            runs.Add(new ClusterRun(runStart, totalClusters - runStart));
        }

        return runs;
    }

    /// <summary>
    /// Copies <paramref name="length"/> bytes from <paramref name="source"/> to <paramref name="target"/>, in chunks that go
    /// through <paramref name="buffer"/>.
    /// </summary>
    public static unsafe void CopyRange(
        SafeDeviceHandle source,
        long sourceOffset,
        SafeDeviceHandle target,
        long targetOffset,
        long length,
        AlignedBuffer buffer)
    {
        if (sourceOffset < 0 || targetOffset < 0 || length < 0 ||
            sourceOffset % SectorBytes != 0 || targetOffset % SectorBytes != 0 || length % SectorBytes != 0)
        {
            throw new ArgumentException("Offsets and lengths must be non-negative multiples of 512 bytes.");
        }

        long copied = 0;
        while (copied < length)
        {
            int chunk = (int)Math.Min(buffer.Length, length - copied);
            fixed (byte* pointer = buffer.Span)
            {
                SeekTo(source, sourceOffset + copied);
                ReadExactly(source, pointer, chunk);
                SeekTo(target, targetOffset + copied);
                WriteExactly(target, pointer, chunk);
            }

            copied += chunk;
        }
    }

    /// <summary>IOCTL_DISK_GET_LENGTH_INFO: the length of a device or volume in bytes. Throws when the device refuses it.</summary>
    public static unsafe long GetDeviceLength(SafeDeviceHandle device)
    {
        GET_LENGTH_INFORMATION info = default;
        bool ok = Kernel32.DeviceIoControl(
            device,
            IoControlCodes.IOCTL_DISK_GET_LENGTH_INFO,
            null,
            0,
            &info,
            (uint)sizeof(GET_LENGTH_INFORMATION),
            out uint returned,
            IntPtr.Zero);

        if (!ok)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "IOCTL_DISK_GET_LENGTH_INFO failed.");
        }

        if (returned < (uint)sizeof(GET_LENGTH_INFORMATION))
        {
            throw new InvalidOperationException($"IOCTL_DISK_GET_LENGTH_INFO returned only {returned} bytes.");
        }

        return info.Length;
    }

    /// <summary>
    /// Reads <paramref name="length"/> bytes at <paramref name="sourceOffset"/> through <paramref name="buffer"/> and returns
    /// them as a managed array. Offset and length must be multiples of 512 bytes.
    /// </summary>
    public static unsafe byte[] ReadBytes(SafeDeviceHandle source, long sourceOffset, int length, AlignedBuffer buffer)
    {
        CheckSectorAligned(sourceOffset, length, buffer);

        fixed (byte* pointer = buffer.Span)
        {
            SeekTo(source, sourceOffset);
            ReadExactly(source, pointer, length);
        }

        byte[] result = new byte[length];
        buffer.Span[..length].CopyTo(result);
        return result;
    }

    /// <summary>
    /// Writes <paramref name="data"/> at <paramref name="targetOffset"/> through <paramref name="buffer"/>. Offset and length
    /// must be multiples of 512 bytes, and the data must fit in the buffer. The caller passes a handle from
    /// <see cref="DiskWriteGuard"/>.
    /// </summary>
    public static unsafe void WriteBytes(SafeDeviceHandle target, long targetOffset, ReadOnlySpan<byte> data, AlignedBuffer buffer)
    {
        CheckSectorAligned(targetOffset, data.Length, buffer);

        data.CopyTo(buffer.Span);
        fixed (byte* pointer = buffer.Span)
        {
            SeekTo(target, targetOffset);
            WriteExactly(target, pointer, data.Length);
        }
    }

    /// <summary>
    /// Checks that a 512-byte sector is a primary NTFS boot sector: OEM ID "NTFS    " at offset 3 and the 0x55 0xAA signature
    /// at offsets 510 and 511. Throws when it is not, so a broken primary is never copied to the backup position.
    /// </summary>
    public static void ValidateNtfsBootSector(ReadOnlySpan<byte> sector)
    {
        if (sector.Length != SectorBytes)
        {
            throw new InvalidOperationException($"The boot sector is {sector.Length} bytes; expected {SectorBytes}.");
        }

        if (!sector.Slice(3, 8).SequenceEqual("NTFS    "u8))
        {
            throw new InvalidOperationException("The primary boot sector has no NTFS OEM ID at offset 3; the backup cannot be synthesized.");
        }

        if (sector[510] != 0x55 || sector[511] != 0xAA)
        {
            throw new InvalidOperationException("The primary boot sector has no 0x55 0xAA signature; the backup cannot be synthesized.");
        }
    }

    private static void CheckSectorAligned(long offset, long length, AlignedBuffer buffer)
    {
        if (offset < 0 || length <= 0 || offset % SectorBytes != 0 || length % SectorBytes != 0 || length > buffer.Length)
        {
            throw new ArgumentException("Offset must be a non-negative multiple of 512 bytes, and length a positive multiple of 512 that fits the buffer.");
        }
    }

    /// <summary>
    /// Opens a volume with read and write access and calls FSCTL_LOCK_VOLUME and FSCTL_DISMOUNT_VOLUME on it. The caller
    /// keeps the handle open for as long as the lock must hold, then calls <see cref="UnlockAndClose"/>.
    /// </summary>
    /// <param name="volumePath">Volume GUID path, for example <c>\\?\Volume{...}</c>.</param>
    public static SafeDeviceHandle LockAndDismount(string volumePath)
    {
        SafeDeviceHandle volume = OpenDevice(volumePath, Kernel32.GENERIC_READ | GenericWrite, 0);
        try
        {
            ControlWithoutData(volume, IoControlCodes.FSCTL_LOCK_VOLUME, "FSCTL_LOCK_VOLUME");
            ControlWithoutData(volume, IoControlCodes.FSCTL_DISMOUNT_VOLUME, "FSCTL_DISMOUNT_VOLUME");
            return volume;
        }
        catch
        {
            volume.Dispose();
            throw;
        }
    }

    /// <summary>
    /// FSCTL_ALLOW_EXTENDED_DASD_IO on a mounted volume handle. Lets reads reach the sectors after the last NTFS cluster,
    /// which NTFS otherwise refuses with a short read.
    /// </summary>
    public static unsafe void AllowExtendedDasdIo(SafeDeviceHandle h)
    {
        if (!Kernel32.DeviceIoControl(h, IoControlCodes.FSCTL_ALLOW_EXTENDED_DASD_IO, null, 0, null, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), "FSCTL_ALLOW_EXTENDED_DASD_IO failed.");
        }
    }

    /// <summary>Releases the lock taken by <see cref="LockAndDismount"/> and closes the handle.</summary>
    public static void UnlockAndClose(SafeDeviceHandle volume)
    {
        try
        {
            ControlWithoutData(volume, IoControlCodes.FSCTL_UNLOCK_VOLUME, "FSCTL_UNLOCK_VOLUME");
        }
        finally
        {
            volume.Dispose();
        }
    }

    private static unsafe void ControlWithoutData(SafeDeviceHandle handle, uint controlCode, string name)
    {
        if (!Kernel32.DeviceIoControl(handle, controlCode, null, 0, null, 0, out _, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"{name} failed.");
        }
    }

    private static void SeekTo(SafeHandle handle, long offset)
    {
        if (!SpikeWin32.SetFilePointerEx(handle, offset, IntPtr.Zero, SpikeWin32.FileBegin))
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Seek to offset {offset} failed.");
        }
    }

    private static unsafe void ReadExactly(SafeHandle handle, byte* buffer, int count)
    {
        int done = 0;
        while (done < count)
        {
            if (!SpikeWin32.ReadFile(handle, buffer + done, (uint)(count - done), out uint read, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "ReadFile failed.");
            }

            if (read == 0)
            {
                throw new EndOfStreamException("The device ended before the requested bytes were read.");
            }

            done += (int)read;
        }
    }

    private static unsafe void WriteExactly(SafeHandle handle, byte* buffer, int count)
    {
        int done = 0;
        while (done < count)
        {
            if (!SpikeWin32.WriteFile(handle, buffer + done, (uint)(count - done), out uint written, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "WriteFile failed.");
            }

            if (written == 0)
            {
                throw new IOException("The device accepted no bytes.");
            }

            done += (int)written;
        }
    }
}
