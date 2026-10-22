using System.ComponentModel;
using System.Globalization;
using Microsoft.Win32.SafeHandles;
using WinClonerX.Domain.Shared;

namespace WinClonerX.Infrastructure.IntegrationTests.Fixtures;

/// <summary>A VHDX attached as a physical disk. <see cref="DiskNumber"/> is N in <c>\\.\PhysicalDriveN</c>.</summary>
internal sealed record AttachedVirtualDisk(string VhdxPath, int DiskNumber, SafeFileHandle Handle);

/// <summary>
/// Creates, attaches and destroys dynamic VHDX files under <c>%TEMP%\WinClonerXTests\</c>. Each attached disk is
/// registered in <see cref="VirtualDiskRegistry"/>, which is what lets <see cref="VirtualDisksOnlyRestriction"/>
/// allow writes to it. Needs an elevated process.
/// </summary>
internal sealed class VirtualDiskFixture : IAsyncDisposable
{
    private const string PhysicalDrivePrefix = @"\\.\PhysicalDrive";
    private const int PhysicalPathBufferChars = 260;
    private const int CleanupAttempts = 5;
    private static readonly TimeSpan _cleanupRetryDelay = TimeSpan.FromMilliseconds(200);

    private readonly object _gate = new();
    private readonly List<AttachedVirtualDisk> _disks = [];
    private bool _disposed;

    public VirtualDiskFixture()
    {
        RootFolder = Path.Combine(Path.GetTempPath(), "WinClonerXTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(RootFolder);
    }

    /// <summary>Folder that holds this fixture's VHDX files. <see cref="DisposeAsync"/> deletes it.</summary>
    public string RootFolder { get; }

    /// <summary>
    /// Creates a dynamic VHDX of <paramref name="size"/> under <see cref="RootFolder"/>, attaches it without a drive
    /// letter, and registers its disk number.
    /// </summary>
    public Task<AttachedVirtualDisk> CreateAndAttachAsync(
        ByteSize size,
        int logicalSectorSize = 512,
        int physicalSectorSize = 4096) =>
        Task.Run(() => CreateAndAttach(size, logicalSectorSize, physicalSectorSize));

    /// <summary>
    /// Detaches every disk, closes its handle and deletes its file, then deletes the root folder. Failures are
    /// logged, never thrown.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        List<AttachedVirtualDisk> disks;
        lock (_gate)
        {
            if (_disposed)
            {
                return ValueTask.CompletedTask;
            }

            _disposed = true;
            disks = [.. _disks];
            _disks.Clear();
        }

        foreach (AttachedVirtualDisk disk in disks)
        {
            // Unregister first, so that no write can be authorized for a disk that is being torn down.
            VirtualDiskRegistry.Unregister(disk.DiskNumber);
            ReleaseDisk(disk.VhdxPath, disk.Handle);
        }

        TryCleanup(
            () =>
            {
                if (Directory.Exists(RootFolder))
                {
                    Directory.Delete(RootFolder, recursive: true);
                }
            },
            $"delete folder '{RootFolder}'");

        return ValueTask.CompletedTask;
    }

    private AttachedVirtualDisk CreateAndAttach(ByteSize size, int logicalSectorSize, int physicalSectorSize)
    {
        if (size.Bytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(size), size, "The disk size must be positive.");
        }

        if (logicalSectorSize is not (512 or 4096))
        {
            throw new ArgumentOutOfRangeException(
                nameof(logicalSectorSize), logicalSectorSize, "The logical sector size must be 512 or 4096 bytes.");
        }

        if (physicalSectorSize is not (512 or 4096) || physicalSectorSize < logicalSectorSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(physicalSectorSize),
                physicalSectorSize,
                "The physical sector size must be 512 or 4096 bytes and not smaller than the logical size.");
        }

        string vhdxPath = Path.Combine(RootFolder, $"disk-{Guid.NewGuid():N}.vhdx");
        EnsureUnderRoot(vhdxPath);

        VIRTUAL_STORAGE_TYPE storageType = VirtDisk.VhdxStorageType();
        var createParameters = new CREATE_VIRTUAL_DISK_PARAMETERS
        {
            Version = VirtDisk.CREATE_VIRTUAL_DISK_VERSION_2,
            UniqueId = Guid.NewGuid(),
            MaximumSize = (ulong)size.Bytes,
            SectorSizeInBytes = (uint)logicalSectorSize,
            PhysicalSectorSizeInBytes = (uint)physicalSectorSize,
        };

        uint error = VirtDisk.CreateVirtualDisk(
            ref storageType,
            vhdxPath,
            VirtDisk.VIRTUAL_DISK_ACCESS_NONE,
            IntPtr.Zero,
            VirtDisk.CREATE_VIRTUAL_DISK_FLAG_NONE,
            0,
            ref createParameters,
            IntPtr.Zero,
            out SafeFileHandle handle);

        if (error != 0)
        {
            handle.Dispose();
            TryCleanup(() => File.Delete(vhdxPath), $"delete '{vhdxPath}'");
            throw Win32Failure(error, "CreateVirtualDisk", vhdxPath);
        }

        var attachParameters = new ATTACH_VIRTUAL_DISK_PARAMETERS
        {
            Version = VirtDisk.ATTACH_VIRTUAL_DISK_VERSION_1,
            Reserved = 0,
        };

        error = VirtDisk.AttachVirtualDisk(
            handle,
            IntPtr.Zero,
            VirtDisk.ATTACH_VIRTUAL_DISK_FLAG_NO_DRIVE_LETTER,
            0,
            ref attachParameters,
            IntPtr.Zero);

        if (error != 0)
        {
            handle.Dispose();
            TryCleanup(() => File.Delete(vhdxPath), $"delete '{vhdxPath}'");
            throw Win32Failure(error, "AttachVirtualDisk", vhdxPath);
        }

        int diskNumber;
        try
        {
            diskNumber = ResolveDiskNumber(vhdxPath, handle);
        }
        catch
        {
            ReleaseDisk(vhdxPath, handle);
            throw;
        }

        var disk = new AttachedVirtualDisk(vhdxPath, diskNumber, handle);
        VirtualDiskRegistry.Register(diskNumber);

        bool accepted;
        lock (_gate)
        {
            accepted = !_disposed;
            if (accepted)
            {
                _disks.Add(disk);
            }
        }

        if (!accepted)
        {
            VirtualDiskRegistry.Unregister(diskNumber);
            ReleaseDisk(vhdxPath, handle);
            throw new ObjectDisposedException(nameof(VirtualDiskFixture));
        }

        return disk;
    }

    private void EnsureUnderRoot(string path)
    {
        string root = Path.GetFullPath(RootFolder) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);

        if (!fullPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"'{fullPath}' is outside the test folder '{root}'.");
        }
    }

    private static unsafe int ResolveDiskNumber(string vhdxPath, SafeFileHandle handle)
    {
        var buffer = new char[PhysicalPathBufferChars];
        uint sizeInBytes = (uint)(buffer.Length * sizeof(char));
        uint error;

        fixed (char* pathPointer = buffer)
        {
            error = VirtDisk.GetVirtualDiskPhysicalPath(handle, ref sizeInBytes, pathPointer);
        }

        if (error != 0)
        {
            throw Win32Failure(error, "GetVirtualDiskPhysicalPath", vhdxPath);
        }

        int terminator = Array.IndexOf(buffer, '\0');
        string physicalPath = new string(buffer, 0, terminator < 0 ? buffer.Length : terminator);
        return ParseDiskNumber(physicalPath, vhdxPath);
    }

    private static int ParseDiskNumber(string physicalPath, string vhdxPath)
    {
        if (!physicalPath.StartsWith(PhysicalDrivePrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unexpected physical path '{physicalPath}' for '{vhdxPath}'.");
        }

        ReadOnlySpan<char> digits = physicalPath.AsSpan(PhysicalDrivePrefix.Length);
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out int diskNumber) || diskNumber < 0)
        {
            throw new InvalidOperationException($"Unexpected physical path '{physicalPath}' for '{vhdxPath}'.");
        }

        return diskNumber;
    }

    /// <summary>Detaches a disk, closes its handle and deletes its file. Never throws.</summary>
    private static void ReleaseDisk(string vhdxPath, SafeFileHandle handle)
    {
        try
        {
            uint error = VirtDisk.DetachVirtualDisk(handle, 0, 0);
            if (error != 0)
            {
                Warn($"DetachVirtualDisk failed for '{vhdxPath}' with Win32 error {error}.");
            }
        }
        catch (Exception ex)
        {
            Warn($"DetachVirtualDisk threw for '{vhdxPath}': {ex.Message}");
        }
        finally
        {
            handle.Dispose();
        }

        TryCleanup(() => File.Delete(vhdxPath), $"delete '{vhdxPath}'");
    }

    /// <summary>
    /// Runs a file-system cleanup action, retrying while antivirus or the OS still holds a file. Logs the last
    /// failure instead of throwing.
    /// </summary>
    private static void TryCleanup(Action action, string description)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                action();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt >= CleanupAttempts)
                {
                    Warn($"Could not {description}: {ex.Message}");
                    return;
                }

                Thread.Sleep(_cleanupRetryDelay);
            }
        }
    }

    private static Win32Exception Win32Failure(uint error, string operation, string vhdxPath) =>
        new((int)error, $"{operation} failed for '{vhdxPath}'.");

    private static void Warn(string message) =>
        Console.Error.WriteLine($"[VirtualDiskFixture] {message}");
}
