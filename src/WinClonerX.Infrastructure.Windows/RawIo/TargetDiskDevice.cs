using System.ComponentModel;
using System.Runtime.InteropServices;
using WinClonerX.Application.Errors;
using WinClonerX.Infrastructure.Windows.Interop;

namespace WinClonerX.Infrastructure.Windows.RawIo
{
    /// <summary>
    /// Device operations needed by <see cref="TargetDisk"/>. The real implementation is <see cref="Win32TargetDiskDevice"/>;
    /// unit tests replace it with a fake, so no device is touched by the tests.
    /// </summary>
    internal interface ITargetDiskDevice : IDisposable
    {
        /// <summary>Length of the disk in bytes, from IOCTL_DISK_GET_LENGTH_INFO.</summary>
        long Length { get; }

        /// <summary>Logical sector size in bytes (512 or 4096), from IOCTL_DISK_GET_DRIVE_GEOMETRY_EX.</summary>
        int LogicalSectorSize { get; }

        /// <summary>Sets or clears the non-persistent offline attribute (IOCTL_DISK_SET_DISK_ATTRIBUTES, Persist = 0).</summary>
        void SetOffline(bool offline);

        /// <summary>Reads the offline attribute (IOCTL_DISK_GET_DISK_ATTRIBUTES).</summary>
        bool IsOffline();

        /// <summary>Writes an aligned buffer at an absolute disk offset.</summary>
        void Write(long offset, ReadOnlySpan<byte> aligned);

        /// <summary>Reads an aligned buffer from an absolute disk offset and returns the number of bytes read.</summary>
        int Read(long offset, Span<byte> aligned);

        /// <summary>Asks Windows to read the disk again (IOCTL_DISK_UPDATE_PROPERTIES).</summary>
        void UpdateProperties();

        /// <summary>GUID of the disk when it has a GPT (IOCTL_DISK_GET_DRIVE_LAYOUT_EX); null when it is not GPT.</summary>
        Guid? GptDiskId();

        /// <summary>Locks and dismounts every volume that lies entirely on this disk. Fails with TargetInUse.</summary>
        IReadOnlyList<IVolumeLock> LockVolumesOnDisk();
    }

    /// <summary>A volume lock taken on the target. Dispose unlocks the volume and closes its handle.</summary>
    internal interface IVolumeLock : IDisposable
    {
        /// <summary>Device path of the locked volume, for example <c>\\?\Volume{...}</c>.</summary>
        string VolumePath { get; }
    }

    /// <summary>Lists the volumes of the system. The real implementation is <see cref="Win32VolumeEnumerator"/>.</summary>
    internal interface IVolumeEnumerator
    {
        /// <summary>Device names of every volume, with the trailing backslash, for example <c>\\?\Volume{...}\</c>.</summary>
        IReadOnlyList<string> VolumeNames();
    }

    /// <summary>
    /// Decides which volumes must be locked for a target disk, and locks them. The rules are:
    /// <list type="bullet">
    /// <item>A volume that is not a disk volume (its extents query fails with ERROR_INVALID_FUNCTION, ERROR_NOT_READY or
    /// ERROR_NOT_SUPPORTED) cannot be a partition of any disk, so it is skipped with a warning.</item>
    /// <item>A volume whose extents lie entirely on other disks is skipped.</item>
    /// <item>A volume that spans the target and another disk is refused with TargetInUse.</item>
    /// <item>Any other failure while classifying a volume, or while locking it, is TargetInUse (fail closed).</item>
    /// </list>
    /// If a failure happens after some volumes were locked, those locks are released before the exception leaves.
    /// </summary>
    internal sealed class TargetVolumeLocker
    {
        internal const int ErrorInvalidFunction = 1;
        internal const int ErrorNotReady = 21;
        internal const int ErrorNotSupported = 50;

        private readonly IVolumeEnumerator _enumerator;
        private readonly IVolumeExtentsReader _extents;
        private readonly Func<string, IVolumeLock> _lockVolume;
        private readonly Action<string>? _onWarning;

        public TargetVolumeLocker(
            IVolumeEnumerator enumerator,
            IVolumeExtentsReader extents,
            Func<string, IVolumeLock> lockVolume,
            Action<string>? onWarning)
        {
            ArgumentNullException.ThrowIfNull(enumerator);
            ArgumentNullException.ThrowIfNull(extents);
            ArgumentNullException.ThrowIfNull(lockVolume);

            _enumerator = enumerator;
            _extents = extents;
            _lockVolume = lockVolume;
            _onWarning = onWarning;
        }

        /// <summary>Whether a volume-extents failure with this Win32 error means the volume is not on any disk.</summary>
        internal static bool IsNotDiskVolume(int nativeError) =>
            nativeError is ErrorInvalidFunction or ErrorNotReady or ErrorNotSupported;

        /// <summary>Locks every volume that lies entirely on <paramref name="diskNumber"/>.</summary>
        public IReadOnlyList<IVolumeLock> LockVolumesOnDisk(int diskNumber)
        {
            ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

            var taken = new List<IVolumeLock>();
            try
            {
                foreach (string volumeName in _enumerator.VolumeNames())
                {
                    string path = volumeName.TrimEnd('\\');
                    IReadOnlySet<uint>? disks = TryDiskNumbersOf(path);
                    if (disks is null || !disks.Contains((uint)diskNumber))
                    {
                        continue;
                    }

                    // A volume that spans the target and another disk cannot be locked as a whole, and writing under
                    // it would corrupt it. Refuse instead of skipping it.
                    if (disks.Count != 1)
                    {
                        throw new PlatformException(
                            PlatformErrorCode.TargetInUse,
                            $"Volume {path} spans disk {diskNumber} and other disks.");
                    }

                    taken.Add(_lockVolume(path));
                }
            }
            catch
            {
                foreach (IVolumeLock volumeLock in taken)
                {
                    volumeLock.Dispose();
                }

                throw;
            }

            return taken;
        }

        /// <summary>Returns the disks of a volume, or null when the volume is not a disk volume and must be skipped.</summary>
        private IReadOnlySet<uint>? TryDiskNumbersOf(string path)
        {
            try
            {
                return _extents.DiskNumbersOf(path);
            }
            catch (Win32Exception ex) when (IsNotDiskVolume(ex.NativeErrorCode))
            {
                _onWarning?.Invoke($"Skipped volume {path}: it is not on a disk (Windows error {ex.NativeErrorCode}).");
                return null;
            }
            catch (Exception ex) when (ex is not PlatformException)
            {
                // Fail closed: a volume whose disk can not be determined might be on the target.
                throw new PlatformException(
                    PlatformErrorCode.TargetInUse,
                    $"Could not read the disk extents of volume {path}.",
                    ex);
            }
        }
    }

    /// <summary>
    /// Win32 implementation of <see cref="ITargetDiskDevice"/>. It takes ownership of the handle returned by
    /// <see cref="DiskWriteGuard.OpenDiskForWrite"/>, and that handle is the only write handle to the disk.
    /// Volume handles are opened here only for lock and dismount, and only for volumes that lie entirely on this disk.
    /// </summary>
    internal sealed unsafe class Win32TargetDiskDevice : ITargetDiskDevice
    {
        private const int GeometryBufferBytes = 64;

        // DRIVE_LAYOUT_INFORMATION_EX header (48 bytes) followed by up to 128 PARTITION_INFORMATION_EX entries (R4, S1).
        private const int DriveLayoutHeaderBytes = 48;
        private const int MaxPartitionEntries = 128;
        private const int DriveLayoutBufferBytes =
            DriveLayoutHeaderBytes + MaxPartitionEntries * IoControlConstants.PARTITION_INFORMATION_EX_SIZE;
        private const uint GptPartitionStyle = 1;

        private readonly SafeDeviceHandle _handle;
        private readonly int _diskNumber;
        private readonly TargetVolumeLocker _volumeLocker;
        private readonly List<IVolumeLock> _volumeLocks = [];
        private bool _disposed;

        /// <summary>
        /// Takes ownership of <paramref name="handle"/>, even when the constructor fails. <paramref name="onWarning"/>
        /// receives a message for every volume that is skipped because it is not on a disk.
        /// </summary>
        public Win32TargetDiskDevice(
            SafeDeviceHandle handle,
            int diskNumber,
            IVolumeExtentsReader extents,
            Action<string>? onWarning = null)
        {
            ArgumentNullException.ThrowIfNull(handle);
            ArgumentNullException.ThrowIfNull(extents);

            _handle = handle;
            try
            {
                ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);
                if (handle.IsInvalid)
                {
                    throw new ArgumentException("The disk handle is invalid.", nameof(handle));
                }

                _diskNumber = diskNumber;
                _volumeLocker = new TargetVolumeLocker(
                    new Win32VolumeEnumerator(),
                    extents,
                    path => LockAndDismount(path),
                    onWarning);
                Length = ReadLength();
                LogicalSectorSize = ReadLogicalSectorSize();
            }
            catch
            {
                handle.Dispose();
                throw;
            }
        }

        public long Length { get; }

        public int LogicalSectorSize { get; }

        public void SetOffline(bool offline)
        {
            ThrowIfDisposed();

            var input = new SET_DISK_ATTRIBUTES
            {
                Version = (uint)sizeof(SET_DISK_ATTRIBUTES),
                Persist = 0,
                Attributes = offline ? IoControlConstants.DISK_ATTRIBUTE_OFFLINE : 0UL,
                AttributesMask = IoControlConstants.DISK_ATTRIBUTE_OFFLINE,
            };

            Ioctl(
                _handle,
                IoControlCodes.IOCTL_DISK_SET_DISK_ATTRIBUTES,
                &input,
                (uint)sizeof(SET_DISK_ATTRIBUTES),
                null,
                0,
                offline ? "set offline" : "set online");
        }

        public bool IsOffline()
        {
            ThrowIfDisposed();

            var attributes = new GET_DISK_ATTRIBUTES();
            Ioctl(
                _handle,
                IoControlCodes.IOCTL_DISK_GET_DISK_ATTRIBUTES,
                null,
                0,
                &attributes,
                (uint)sizeof(GET_DISK_ATTRIBUTES),
                "query disk attributes");

            return (attributes.Attributes & IoControlConstants.DISK_ATTRIBUTE_OFFLINE) != 0;
        }

        public void Write(long offset, ReadOnlySpan<byte> aligned)
        {
            ThrowIfDisposed();
            if (aligned.IsEmpty)
            {
                return;
            }

            Seek(offset);
            fixed (byte* pointer = aligned)
            {
                if (!Kernel32.WriteFile(_handle, pointer, (uint)aligned.Length, out uint written, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"WriteFile at {offset} failed.");
                }

                if (written != aligned.Length)
                {
                    throw new IOException($"Short write at {offset}: {written} of {aligned.Length} bytes.");
                }
            }
        }

        public int Read(long offset, Span<byte> aligned)
        {
            ThrowIfDisposed();
            if (aligned.IsEmpty)
            {
                return 0;
            }

            Seek(offset);
            fixed (byte* pointer = aligned)
            {
                if (!Kernel32.ReadFile(_handle, pointer, (uint)aligned.Length, out uint read, IntPtr.Zero))
                {
                    throw new Win32Exception(Marshal.GetLastPInvokeError(), $"ReadFile at {offset} failed.");
                }

                return (int)read;
            }
        }

        public void UpdateProperties()
        {
            ThrowIfDisposed();
            Ioctl(_handle, IoControlCodes.IOCTL_DISK_UPDATE_PROPERTIES, null, 0, null, 0, "update properties");
        }

        public Guid? GptDiskId()
        {
            ThrowIfDisposed();

            byte[] buffer = new byte[DriveLayoutBufferBytes];
            uint returned;
            fixed (byte* pointer = buffer)
            {
                returned = Ioctl(
                    _handle,
                    IoControlCodes.IOCTL_DISK_GET_DRIVE_LAYOUT_EX,
                    null,
                    0,
                    pointer,
                    (uint)buffer.Length,
                    "read drive layout");
            }

            if (returned < DriveLayoutHeaderBytes)
            {
                throw new InvalidOperationException($"Drive layout reply for disk {_diskNumber} is too short.");
            }

            DRIVE_LAYOUT_INFORMATION_EX header = MemoryMarshal.Read<DRIVE_LAYOUT_INFORMATION_EX>(buffer);
            return header.PartitionStyle == GptPartitionStyle ? header.Gpt.DiskId : null;
        }

        public IReadOnlyList<IVolumeLock> LockVolumesOnDisk()
        {
            ThrowIfDisposed();

            IReadOnlyList<IVolumeLock> taken = _volumeLocker.LockVolumesOnDisk(_diskNumber);
            _volumeLocks.AddRange(taken);
            return taken;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            foreach (IVolumeLock volumeLock in _volumeLocks)
            {
                volumeLock.Dispose();
            }

            _volumeLocks.Clear();
            _handle.Dispose();
        }

        private long ReadLength()
        {
            var info = new GET_LENGTH_INFORMATION();
            Ioctl(_handle, IoControlCodes.IOCTL_DISK_GET_LENGTH_INFO, null, 0, &info, (uint)sizeof(GET_LENGTH_INFORMATION), "read length");
            if (info.Length <= 0)
            {
                throw new InvalidOperationException($"Disk {_diskNumber} reports a non-positive length.");
            }

            return info.Length;
        }

        private int ReadLogicalSectorSize()
        {
            byte[] buffer = new byte[GeometryBufferBytes];
            fixed (byte* pointer = buffer)
            {
                Ioctl(
                    _handle,
                    IoControlCodes.IOCTL_DISK_GET_DRIVE_GEOMETRY_EX,
                    null,
                    0,
                    pointer,
                    (uint)buffer.Length,
                    "read geometry");
            }

            uint sector = MemoryMarshal.Read<DISK_GEOMETRY_EX_HEADER>(buffer).BytesPerSector;
            if (sector is not (512 or 4096))
            {
                throw new InvalidOperationException($"Disk {_diskNumber} has an unsupported logical sector size {sector}.");
            }

            return (int)sector;
        }

        private void Seek(long offset)
        {
            if (!Kernel32.SetFilePointerEx(_handle, offset, out _, Kernel32.FILE_BEGIN))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Seek to {offset} failed.");
            }
        }

        private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

        private static Win32VolumeLock LockAndDismount(string path)
        {
            SafeDeviceHandle handle = Kernel32.CreateFileW(
                path,
                Kernel32.GENERIC_READ | Kernel32.GENERIC_WRITE,
                Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
                IntPtr.Zero,
                Kernel32.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle.IsInvalid)
            {
                int error = Marshal.GetLastPInvokeError();
                handle.Dispose();
                throw new PlatformException(
                    PlatformErrorCode.TargetInUse,
                    $"Could not open volume {path}.",
                    new Win32Exception(error));
            }

            try
            {
                if (!Kernel32.DeviceIoControl(handle, IoControlCodes.FSCTL_LOCK_VOLUME, null, 0, null, 0, out _, IntPtr.Zero))
                {
                    throw new PlatformException(
                        PlatformErrorCode.TargetInUse,
                        $"Could not lock volume {path}.",
                        new Win32Exception(Marshal.GetLastPInvokeError()));
                }

                if (!Kernel32.DeviceIoControl(handle, IoControlCodes.FSCTL_DISMOUNT_VOLUME, null, 0, null, 0, out _, IntPtr.Zero))
                {
                    throw new PlatformException(
                        PlatformErrorCode.TargetInUse,
                        $"Could not dismount volume {path}.",
                        new Win32Exception(Marshal.GetLastPInvokeError()));
                }
            }
            catch
            {
                // Closing the handle also releases a lock that was taken.
                handle.Dispose();
                throw;
            }

            return new Win32VolumeLock(path, handle);
        }

        private uint Ioctl(
            SafeDeviceHandle handle,
            uint code,
            void* input,
            uint inputSize,
            void* output,
            uint outputSize,
            string operation)
        {
            if (!Kernel32.DeviceIoControl(handle, code, input, inputSize, output, outputSize, out uint returned, IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Disk {_diskNumber}: {operation} failed.");
            }

            return returned;
        }
    }

    /// <summary>Win32 implementation of <see cref="IVolumeEnumerator"/> over FindFirstVolumeW and FindNextVolumeW.</summary>
    internal sealed unsafe class Win32VolumeEnumerator : IVolumeEnumerator
    {
        private const int ErrorNoMoreFiles = 18;
        private const int VolumeNameChars = 260;
        private static readonly IntPtr InvalidFindHandle = new(-1);

        public IReadOnlyList<string> VolumeNames()
        {
            var names = new List<string>();
            char* buffer = stackalloc char[VolumeNameChars];

            IntPtr find = Kernel32.FindFirstVolumeW(buffer, VolumeNameChars);
            if (find == InvalidFindHandle)
            {
                throw new Win32Exception(Marshal.GetLastPInvokeError(), "FindFirstVolumeW failed.");
            }

            try
            {
                names.Add(new string(buffer));
                while (true)
                {
                    if (!Kernel32.FindNextVolumeW(find, buffer, VolumeNameChars))
                    {
                        int error = Marshal.GetLastPInvokeError();
                        if (error == ErrorNoMoreFiles)
                        {
                            break;
                        }

                        throw new Win32Exception(error, "FindNextVolumeW failed.");
                    }

                    names.Add(new string(buffer));
                }
            }
            finally
            {
                Kernel32.FindVolumeClose(find);
            }

            return names;
        }
    }

    /// <summary>A volume locked and dismounted for the target disk. The handle stays open until <see cref="Dispose"/>.</summary>
    internal sealed unsafe class Win32VolumeLock : IVolumeLock
    {
        private readonly SafeDeviceHandle _handle;
        private bool _released;

        public Win32VolumeLock(string volumePath, SafeDeviceHandle handle)
        {
            VolumePath = volumePath;
            _handle = handle;
        }

        public string VolumePath { get; }

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;

            // Unlock, then close. Closing the handle releases the lock too, so a failed unlock is not an error here.
            _ = Kernel32.DeviceIoControl(_handle, IoControlCodes.FSCTL_UNLOCK_VOLUME, null, 0, null, 0, out _, IntPtr.Zero);
            _handle.Dispose();
        }
    }

    /// <summary>Fixed part of DISK_GEOMETRY_EX (x64). Only the logical sector size is used.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct DISK_GEOMETRY_EX_HEADER
    {
        public long Cylinders;
        public int MediaType;
        public uint TracksPerCylinder;
        public uint SectorsPerTrack;
        public uint BytesPerSector;
        public long DiskSize;
    }
}

namespace WinClonerX.Infrastructure.Windows.Interop
{
    // Declared in the RawIo folder so this change touches only the RawIo files, the same as RawPartitionSource.cs.
    internal static partial class Kernel32
    {
        [LibraryImport("kernel32.dll", SetLastError = true)]
        internal static unsafe partial IntPtr FindFirstVolumeW(char* lpszVolumeName, uint cchBufferLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static unsafe partial bool FindNextVolumeW(IntPtr hFindVolume, char* lpszVolumeName, uint cchBufferLength);

        [LibraryImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static partial bool FindVolumeClose(IntPtr hFindVolume);
    }
}
