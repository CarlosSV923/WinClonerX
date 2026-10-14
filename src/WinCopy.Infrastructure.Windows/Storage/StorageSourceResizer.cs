using System.Globalization;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Logging;
using WinCopy.Infrastructure.Windows.RawIo;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>
/// Shrinks and restores the running Windows partition of the source disk with <c>MSFT_Partition.Resize</c> (R6).
/// </summary>
/// <remarks>
/// <para>
/// <b>Safety rule.</b> Every method first checks that the partition holds the running Windows volume: its volume GUID
/// path is on the same disks as <see cref="IVolumeExtentsReader.WindowsVolumePath"/>, and it carries the Windows drive
/// root (for example <c>C:\</c>). Any other partition is refused before any resize call. This is the inverse of
/// <see cref="RawIo.DiskWriteGuard"/>: here only the Windows partition may be resized.
/// </para>
/// <para>
/// <b>Failures.</b> A shrink or restore that fails can leave the partition changed. The caller must still run
/// <see cref="RestoreAsync"/> (FR-021a). Each resize call runs in one <see cref="DiskOperationScope"/> named
/// <c>Shrink</c> or <c>Restore</c>. A refusal by the safety rule is not a resize, so it is not recorded.
/// </para>
/// <para>
/// <b>Cancellation.</b> The token is checked before the work starts. Once <c>Resize</c> has started it cannot be
/// cancelled, because Windows may already be moving files.
/// </para>
/// </remarks>
internal sealed class StorageSourceResizer : ISourceResizer
{
    private const long Mib = 1L << 20;

    // The exact refusal text is part of the contract: tests and the log look for it.
    private const string RefusalMessage = "refusing to resize a partition that is not the running Windows partition";

    private readonly IPartitionResizeApi _api;
    private readonly IVolumeExtentsReader _extents;
    private readonly DiskOperationRecorder _recorder;
    private readonly IClock _clock;

    public StorageSourceResizer(
        IPartitionResizeApi api,
        IVolumeExtentsReader extents,
        DiskOperationRecorder recorder,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentNullException.ThrowIfNull(extents);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentNullException.ThrowIfNull(clock);

        _api = api;
        _extents = extents;
        _recorder = recorder;
        _clock = clock;
    }

    /// <inheritdoc />
    public Task<ByteSize> GetMinimumSizeAsync(PartitionGuid partition, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // The token is only checked above: the WMI work is not cancellable once started.
        return Task.Run(() => MinimumSize(partition), CancellationToken.None);
    }

    /// <inheritdoc />
    public Task ShrinkAsync(PartitionGuid partition, ByteSize newLength, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Not passed to Task.Run on purpose: once Resize has started it must run to the end, never be abandoned.
        return Task.Run(() => Shrink(partition, newLength), CancellationToken.None);
    }

    /// <inheritdoc />
    public Task RestoreAsync(PartitionGuid partition, ByteSize originalLength, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Not passed to Task.Run on purpose: once Resize has started it must run to the end, never be abandoned.
        return Task.Run(() => Restore(partition, originalLength), CancellationToken.None);
    }

    private ByteSize MinimumSize(PartitionGuid partition)
    {
        PartitionResizeInfo info = RequireRunningWindowsPartition(partition, PlatformErrorCode.ShrinkFailed);

        (long sizeMin, _) = Guard(
            PlatformErrorCode.ShrinkFailed,
            "query the supported size",
            () => _api.GetSupportedSize(info.DiskNumber, info.PartitionNumber));

        return new ByteSize(sizeMin);
    }

    private void Shrink(PartitionGuid partition, ByteSize newLength)
    {
        PartitionResizeInfo info = RequireRunningWindowsPartition(partition, PlatformErrorCode.ShrinkFailed);

        // Query again right before the resize: files on the volume may have changed since the plan was made.
        (long sizeMin, _) = Guard(
            PlatformErrorCode.ShrinkFailed,
            "query the supported size",
            () => _api.GetSupportedSize(info.DiskNumber, info.PartitionNumber));

        long target = newLength.Bytes;

        if (target >= info.Size)
        {
            throw new PlatformException(
                PlatformErrorCode.ShrinkFailed,
                $"the new length of {target} bytes is not smaller than the current {info.Size} bytes.");
        }

        if (target < sizeMin)
        {
            throw new PlatformException(
                PlatformErrorCode.ShrinkFailed,
                $"cannot shrink below the supported minimum of {sizeMin} bytes (requested {target}).");
        }

        using DiskOperationScope scope = _recorder.Begin(PartitionLabel(partition), "Shrink", info.Offset, target);
        try
        {
            GuardAction(
                PlatformErrorCode.ShrinkFailed,
                "resize the partition",
                () => _api.Resize(info.DiskNumber, info.PartitionNumber, target));

            VerifyLength(partition, target, PlatformErrorCode.ShrinkFailed, "shrink");
            scope.Succeed();
        }
        catch (Exception ex)
        {
            scope.Fail(ex);
            throw;
        }
    }

    private void Restore(PartitionGuid partition, ByteSize originalLength)
    {
        PartitionResizeInfo info = RequireRunningWindowsPartition(partition, PlatformErrorCode.RestoreFailed);

        (_, long sizeMax) = Guard(
            PlatformErrorCode.RestoreFailed,
            "query the supported size",
            () => _api.GetSupportedSize(info.DiskNumber, info.PartitionNumber));

        long target = originalLength.Bytes;

        if (target < info.Size)
        {
            throw new PlatformException(
                PlatformErrorCode.RestoreFailed,
                $"the original length of {target} bytes is smaller than the current {info.Size} bytes; a restore cannot shrink the partition.");
        }

        // SizeMax already accounts for the free space right after the partition.
        if (target > sizeMax)
        {
            throw new PlatformException(
                PlatformErrorCode.RestoreFailed,
                $"the original length of {target} bytes exceeds the largest size Windows allows ({sizeMax} bytes).");
        }

        // Already at the original length: there is nothing to resize.
        if (target == info.Size)
        {
            return;
        }

        using DiskOperationScope scope = _recorder.Begin(PartitionLabel(partition), "Restore", info.Offset, target);
        try
        {
            GuardAction(
                PlatformErrorCode.RestoreFailed,
                "resize the partition",
                () => _api.Resize(info.DiskNumber, info.PartitionNumber, target));

            VerifyLength(partition, target, PlatformErrorCode.RestoreFailed, "restore");
            scope.Succeed();
        }
        catch (Exception ex)
        {
            scope.Fail(ex);
            throw;
        }
    }

    /// <summary>
    /// The safety rule. Returns the partition only when it holds the running Windows volume. Every refusal throws
    /// <see cref="PlatformErrorCode.ShrinkFailed"/> with the fixed refusal text, before any resize call. Read failures
    /// use <paramref name="readFailure"/>, the code of the operation that asked.
    /// </summary>
    private PartitionResizeInfo RequireRunningWindowsPartition(PartitionGuid partition, PlatformErrorCode readFailure)
    {
        PartitionResizeInfo info = Guard(readFailure, "read the partition", () => _api.GetPartition(partition.Value));

        string windowsPath = Guard(readFailure, "locate the running Windows volume", () => _extents.WindowsVolumePath());
        char windowsLetter = WindowsDriveLetter(windowsPath, readFailure);
        string windowsRoot = $"{windowsLetter}:\\";

        bool hasWindowsRoot = info.AccessPaths.Any(path =>
            string.Equals(path, windowsRoot, StringComparison.OrdinalIgnoreCase));

        bool letterMatches = info.DriveLetter is null
            || char.ToUpperInvariant(info.DriveLetter.Value) == windowsLetter;

        string[] volumePaths = info.AccessPaths.Where(VolumePath.IsVolumeGuidPath).ToArray();

        if (!hasWindowsRoot || !letterMatches || volumePaths.Length != 1)
        {
            throw new PlatformException(PlatformErrorCode.ShrinkFailed, RefusalMessage);
        }

        // The device path has no trailing backslash.
        string devicePath = VolumePath.ToDevicePath(volumePaths[0]);

        IReadOnlySet<uint> partitionDisks = Guard(
            readFailure,
            "read the disk extents of the partition",
            () => _extents.DiskNumbersOf(devicePath));

        IReadOnlySet<uint> windowsDisks = Guard(
            readFailure,
            "read the disk extents of the Windows volume",
            () => _extents.DiskNumbersOf(windowsPath));

        if (!partitionDisks.SetEquals(windowsDisks) || !windowsDisks.Contains((uint)info.DiskNumber))
        {
            throw new PlatformException(PlatformErrorCode.ShrinkFailed, RefusalMessage);
        }

        return info;
    }

    /// <summary>Reads the drive letter from a path shaped like <c>\\.\C:</c>. Any other shape fails closed.</summary>
    private static char WindowsDriveLetter(string windowsPath, PlatformErrorCode code)
    {
        if (windowsPath.Length != 6
            || !windowsPath.StartsWith(@"\\.\", StringComparison.Ordinal)
            || !char.IsAsciiLetter(windowsPath[4])
            || windowsPath[5] != ':')
        {
            throw new PlatformException(code, $"Unexpected Windows volume path '{windowsPath}'.");
        }

        return char.ToUpperInvariant(windowsPath[4]);
    }

    /// <summary>
    /// Re-reads the partition after a resize and checks that its length is the requested one. Windows may round up to
    /// the next MiB, so a larger length within one MiB is accepted. A smaller length is not.
    /// </summary>
    private void VerifyLength(PartitionGuid partition, long requested, PlatformErrorCode code, string operation)
    {
        PartitionResizeInfo after = Guard(code, "re-read the partition", () => _api.GetPartition(partition.Value));

        long roundedUp = RoundUpToMib(requested);
        if (after.Size < requested || after.Size > roundedUp)
        {
            throw new PlatformException(
                code,
                $"after the {operation} the partition is {after.Size} bytes instead of {requested}.");
        }
    }

    private static long RoundUpToMib(long value)
    {
        long remainder = value % Mib;
        return remainder == 0 ? value : value + (Mib - remainder);
    }

    private static string PartitionLabel(PartitionGuid partition) =>
        partition.Value.ToString("B", CultureInfo.InvariantCulture);

    /// <summary>Runs a WMI or Windows call. Any failure that is not already a <see cref="PlatformException"/> is wrapped.</summary>
    private static T Guard<T>(PlatformErrorCode code, string action, Func<T> call)
    {
        try
        {
            return call();
        }
        catch (PlatformException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PlatformException(code, $"Could not {action}: {ex.Message}", ex);
        }
    }

    /// <summary>The void form of <see cref="Guard{T}"/>.</summary>
    private static void GuardAction(PlatformErrorCode code, string action, Action call)
    {
        try
        {
            call();
        }
        catch (PlatformException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PlatformException(code, $"Could not {action}: {ex.Message}", ex);
        }
    }
}
