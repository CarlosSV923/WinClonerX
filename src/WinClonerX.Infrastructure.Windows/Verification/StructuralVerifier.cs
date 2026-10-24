using System.ComponentModel;
using System.Runtime.InteropServices;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Execution;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Boot;
using WinClonerX.Infrastructure.Windows.Interop;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Storage;

namespace WinClonerX.Infrastructure.Windows.Verification;

/// <summary>Partition table style of a disk, as reported by the layout query.</summary>
internal enum DiskPartitionStyle
{
    Mbr,
    Gpt,
    Raw,
}

/// <summary>A GPT partition of a disk: its unique id, its type, and its extent in bytes.</summary>
internal sealed record DriveLayoutPartition(Guid UniqueId, Guid Type, long StartingOffset, long Length);

/// <summary>Partition table of a disk. Partitions are listed only for GPT disks.</summary>
internal sealed record DriveLayout(DiskPartitionStyle Style, Guid DiskId, IReadOnlyList<DriveLayoutPartition> Partitions);

/// <summary>Reads the partition table of a physical disk. Tests fake it; <see cref="Win32DriveLayoutQuery"/> is the real one.</summary>
internal interface IDriveLayoutQuery
{
    /// <summary>Reads the layout of <c>\\.\PhysicalDrive{diskNumber}</c>. Throws when the read fails.</summary>
    DriveLayout Read(int diskNumber);
}

/// <summary>Checks that a volume can be read as NTFS. It never writes. Tests fake it; <see cref="Win32NtfsVolumeProbe"/> is the real one.</summary>
internal interface INtfsVolumeProbe
{
    /// <summary>Throws when the volume at <paramref name="volumeGuidPath"/> cannot be opened or is not NTFS.</summary>
    void Probe(string volumeGuidPath);
}

/// <summary>A file-exists check. Tests fake it.</summary>
internal interface IFileExistence
{
    bool Exists(string path);
}

/// <summary>
/// Structural verification of a written clone (T071, FR-022). It checks the partition table, that every NTFS volume can
/// be read, and that the EFI system partition holds a BCD store.
/// </summary>
/// <remarks>
/// <para>
/// The three checks are independent: each one adds its issues and the next one runs anyway. A check that throws becomes
/// an issue. The only exception that propagates is a cancellation requested through <paramref name="ct"/>. Nothing is
/// written: the layout is read with no access rights, the volumes with read access only, and the EFI system partition
/// is mounted only for the file check and then released in a finally block.
/// </para>
/// </remarks>
internal sealed class StructuralVerifier : IStructuralVerifier
{
    internal const string BcdRelativePath = @"EFI\Microsoft\Boot\BCD";

    private readonly IDriveLayoutQuery _layout;
    private readonly INtfsVolumeProbe _ntfs;
    private readonly IFileExistence _files;
    private readonly IVolumePathResolver _resolver;
    private readonly ITemporaryMounter _mounter;

    public StructuralVerifier()
        : this(
            new Win32DriveLayoutQuery(),
            new Win32NtfsVolumeProbe(),
            new FileSystemExistence(),
            new StorageVolumePathResolver(),
            new TemporaryMounter(new StorageVolumePathResolver(), new EnvironmentLogicalDriveSource(), new Win32MountPointApi()))
    {
    }

    internal StructuralVerifier(
        IDriveLayoutQuery layout,
        INtfsVolumeProbe ntfs,
        IFileExistence files,
        IVolumePathResolver resolver,
        ITemporaryMounter mounter)
    {
        ArgumentNullException.ThrowIfNull(layout);
        ArgumentNullException.ThrowIfNull(ntfs);
        ArgumentNullException.ThrowIfNull(files);
        ArgumentNullException.ThrowIfNull(resolver);
        ArgumentNullException.ThrowIfNull(mounter);

        _layout = layout;
        _ntfs = ntfs;
        _files = files;
        _resolver = resolver;
        _mounter = mounter;
    }

    /// <inheritdoc />
    public async Task<VerificationResult> VerifyAsync(ClonePlan plan, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var issues = new List<string>();

        await CheckLayoutAsync(plan, issues, ct);
        await CheckVolumesAsync(plan, issues, ct);
        await CheckBootAsync(plan, issues, ct);

        return new VerificationResult(issues.Count == 0, issues);
    }

    private async Task CheckLayoutAsync(ClonePlan plan, List<string> issues, CancellationToken ct)
    {
        int diskNumber = plan.Target.Number;

        DriveLayout layout;
        try
        {
            layout = await Task.Run(() => _layout.Read(diskNumber), ct);
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            issues.Add($"The partition table of disk {diskNumber} could not be read: {ex.Message}");
            return;
        }

        // Without GPT there is no GUID table to compare, so the style is the only finding.
        if (layout.Style != DiskPartitionStyle.Gpt)
        {
            issues.Add($"Disk {diskNumber} does not use a GPT partition table (style {layout.Style}).");
            return;
        }

        if (layout.DiskId != plan.NewDiskGuid)
        {
            issues.Add($"The disk GUID is {layout.DiskId:B}; the plan expects {plan.NewDiskGuid:B}.");
        }

        if (layout.Partitions.Count != plan.Mappings.Count)
        {
            issues.Add($"Disk {diskNumber} has {layout.Partitions.Count} partitions; the plan has {plan.Mappings.Count}.");
        }

        foreach (PartitionMapping mapping in plan.Mappings)
        {
            CheckPartition(mapping, layout, issues);
        }
    }

    private static void CheckPartition(PartitionMapping mapping, DriveLayout layout, List<string> issues)
    {
        int sourceNumber = mapping.Source.Number;
        Guid expectedId = mapping.NewUniqueId.Value;

        DriveLayoutPartition? found = layout.Partitions.FirstOrDefault(partition => partition.UniqueId == expectedId);
        if (found is null)
        {
            issues.Add($"The partition of source partition {sourceNumber} (GUID {expectedId:B}) is missing from the target table.");
            return;
        }

        Guid expectedType = mapping.Source.Type.Value;
        if (found.Type != expectedType)
        {
            issues.Add($"The partition of source partition {sourceNumber} has type {found.Type:B}; the plan expects {expectedType:B}.");
        }

        if (found.StartingOffset != mapping.TargetExtent.Offset)
        {
            issues.Add(
                $"The partition of source partition {sourceNumber} starts at byte {found.StartingOffset}; " +
                $"the plan expects byte {mapping.TargetExtent.Offset}.");
        }

        if (found.Length != mapping.TargetExtent.Length)
        {
            issues.Add(
                $"The partition of source partition {sourceNumber} is {found.Length} bytes long; " +
                $"the plan expects {mapping.TargetExtent.Length} bytes.");
        }
    }

    private async Task CheckVolumesAsync(ClonePlan plan, List<string> issues, CancellationToken ct)
    {
        foreach (PartitionMapping mapping in plan.Mappings)
        {
            if (mapping.Source.Volume?.FileSystem != FileSystemKind.Ntfs)
            {
                continue;
            }

            int sourceNumber = mapping.Source.Number;
            try
            {
                await Task.Run(
                    () =>
                    {
                        string volumePath = _resolver.GetVolumeGuidPath(mapping.NewUniqueId);
                        _ntfs.Probe(volumePath);
                    },
                    ct);
            }
            catch (Exception ex) when (!ct.IsCancellationRequested)
            {
                issues.Add($"The volume of source partition {sourceNumber} is not readable as NTFS: {ex.Message}");
            }
        }
    }

    private async Task CheckBootAsync(ClonePlan plan, List<string> issues, CancellationToken ct)
    {
        PartitionMapping? esp = plan.Mappings.FirstOrDefault(mapping => mapping.Source.Role == PartitionRole.EfiSystem);
        if (esp is null)
        {
            issues.Add("The plan has no EFI system partition, so the boot store cannot be checked.");
            return;
        }

        try
        {
            TemporaryMount mount = await _mounter.MountAsync(esp.NewUniqueId, ct);

            bool hasBcd;
            try
            {
                hasBcd = _files.Exists(Path.Combine(mount.Root, BcdRelativePath));
            }
            finally
            {
                await mount.Release.DisposeAsync();
            }

            if (!hasBcd)
            {
                issues.Add($"The boot store {BcdRelativePath} is missing from the EFI system partition.");
            }
        }
        catch (Exception ex) when (!ct.IsCancellationRequested)
        {
            issues.Add($"The EFI system partition could not be checked: {ex.Message}");
        }
    }
}

/// <summary>
/// Win32 implementation of <see cref="IDriveLayoutQuery"/>. It opens the disk with desiredAccess 0, so it can query but
/// not read or write the disk contents.
/// </summary>
internal sealed class Win32DriveLayoutQuery : IDriveLayoutQuery
{
    // Room for 512 GPT entries. A table with more partitions is refused instead of truncated.
    private const int MaxPartitions = 512;
    private const int HeaderSize = 48;
    private const int BufferSize = HeaderSize + (MaxPartitions * IoControlConstants.PARTITION_INFORMATION_EX_SIZE);

    public unsafe DriveLayout Read(int diskNumber)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(diskNumber);

        using SafeDeviceHandle handle = OpenQueryOnly(PhysicalDisk.PathFor(diskNumber));

        byte[] output = new byte[BufferSize];
        uint returned;
        fixed (byte* outputPointer = output)
        {
            bool ok = Kernel32.DeviceIoControl(
                handle,
                IoControlCodes.IOCTL_DISK_GET_DRIVE_LAYOUT_EX,
                null,
                0,
                outputPointer,
                (uint)output.Length,
                out returned,
                IntPtr.Zero);

            if (!ok)
            {
                throw new Win32Exception(
                    Marshal.GetLastPInvokeError(), $"IOCTL_DISK_GET_DRIVE_LAYOUT_EX failed for disk {diskNumber}.");
            }
        }

        if (returned < HeaderSize)
        {
            throw new InvalidOperationException($"The drive layout of disk {diskNumber} is too short ({returned} bytes).");
        }

        DRIVE_LAYOUT_INFORMATION_EX header = MemoryMarshal.Read<DRIVE_LAYOUT_INFORMATION_EX>(output);
        if (header.PartitionCount > MaxPartitions)
        {
            throw new InvalidOperationException(
                $"Disk {diskNumber} has {header.PartitionCount} partitions; at most {MaxPartitions} can be verified.");
        }

        DiskPartitionStyle style = StyleOf(header.PartitionStyle);
        if (style != DiskPartitionStyle.Gpt)
        {
            return new DriveLayout(style, Guid.Empty, []);
        }

        int count = (int)header.PartitionCount;
        if (HeaderSize + ((long)count * IoControlConstants.PARTITION_INFORMATION_EX_SIZE) > returned)
        {
            throw new InvalidOperationException($"The drive layout of disk {diskNumber} is truncated.");
        }

        var partitions = new List<DriveLayoutPartition>(count);
        for (int i = 0; i < count; i++)
        {
            int offset = HeaderSize + (i * IoControlConstants.PARTITION_INFORMATION_EX_SIZE);
            PARTITION_INFORMATION_EX entry = MemoryMarshal.Read<PARTITION_INFORMATION_EX>(output.AsSpan(offset));

            partitions.Add(new DriveLayoutPartition(
                entry.Gpt.PartitionId,
                entry.Gpt.PartitionType,
                entry.StartingOffset,
                entry.PartitionLength));
        }

        return new DriveLayout(style, header.Gpt.DiskId, partitions);
    }

    private static DiskPartitionStyle StyleOf(uint style) => style switch
    {
        0 => DiskPartitionStyle.Mbr,
        1 => DiskPartitionStyle.Gpt,
        2 => DiskPartitionStyle.Raw,
        _ => throw new InvalidOperationException($"Unknown partition style {style}."),
    };

    private static SafeDeviceHandle OpenQueryOnly(string path)
    {
        SafeDeviceHandle handle = Kernel32.CreateFileW(
            path,
            0,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            int error = Marshal.GetLastPInvokeError();
            handle.Dispose();
            throw new Win32Exception(error, $"Could not open {path} for a layout query.");
        }

        return handle;
    }
}

/// <summary>
/// Win32 implementation of <see cref="INtfsVolumeProbe"/>. It opens the volume with GENERIC_READ only and calls
/// FSCTL_GET_NTFS_VOLUME_DATA. A handle with no access rights is refused for this FSCTL (spike S3), so read access is required.
/// </summary>
internal sealed class Win32NtfsVolumeProbe : INtfsVolumeProbe
{
    public unsafe void Probe(string volumeGuidPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeGuidPath);

        // CreateFile takes the volume GUID path without its trailing backslash.
        string devicePath = volumeGuidPath.TrimEnd('\\');

        using SafeDeviceHandle handle = Kernel32.CreateFileW(
            devicePath,
            Kernel32.GENERIC_READ,
            Kernel32.FILE_SHARE_READ | Kernel32.FILE_SHARE_WRITE,
            IntPtr.Zero,
            Kernel32.OPEN_EXISTING,
            0,
            IntPtr.Zero);

        if (handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastPInvokeError(), $"Could not open {devicePath} for reading.");
        }

        var data = new NTFS_VOLUME_DATA_BUFFER();
        bool ok = Kernel32.DeviceIoControl(
            handle,
            IoControlCodes.FSCTL_GET_NTFS_VOLUME_DATA,
            null,
            0,
            &data,
            (uint)sizeof(NTFS_VOLUME_DATA_BUFFER),
            out uint returned,
            IntPtr.Zero);

        if (!ok)
        {
            throw new Win32Exception(
                Marshal.GetLastPInvokeError(), $"FSCTL_GET_NTFS_VOLUME_DATA failed for {devicePath}.");
        }

        if (returned < (uint)sizeof(NTFS_VOLUME_DATA_BUFFER) || data.BytesPerSector == 0 || data.BytesPerCluster == 0)
        {
            throw new InvalidOperationException($"{devicePath} did not return valid NTFS volume data.");
        }
    }
}

/// <summary>File system implementation of <see cref="IFileExistence"/>.</summary>
internal sealed class FileSystemExistence : IFileExistence
{
    public bool Exists(string path) => File.Exists(path);
}
