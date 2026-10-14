using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Planning;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Gpt;
using WinCopy.Infrastructure.Windows.Logging;

namespace WinCopy.Infrastructure.Windows.RawIo;

/// <summary>
/// Raw read and write access to the target disk, and the atomic commit of its layout (R4, spike S1).
/// </summary>
/// <remarks>
/// <para>Order of the operations, as the orchestrator uses them:</para>
/// <list type="number">
/// <item><see cref="LockAndTakeOfflineAsync"/>: lock and dismount the volumes on the disk, then set the disk offline (non-persistent).</item>
/// <item><see cref="WipeBoundariesAsync"/>: zero the first and the last MiB, which destroys any old MBR and GPT.</item>
/// <item><see cref="WriteAsync"/>: copy the data. Writes are refused while the disk is online.</item>
/// <item><see cref="CommitLayoutAsync"/>: write the backup entries, the backup header, the primary entries, the protective
/// MBR, and the primary header last. Until the primary header lands, the disk has no valid layout.</item>
/// <item><see cref="BringOnlineAsync"/>: set the disk online, ask Windows to read it again, then release the volume locks.</item>
/// </list>
/// <para>
/// <see cref="DeleteLayoutAsync"/> is the compensation: it zeros both ends, so the disk shows as uninitialized.
/// <see cref="DisposeAsync"/> releases the locks and brings the disk online if it is still offline.
/// Errors from the device are translated to <see cref="PlatformException"/> codes.
/// </para>
/// </remarks>
internal sealed class TargetDisk : ITargetDisk
{
    /// <summary>Size of each wiped boundary: the first and the last MiB (R4, step 3).</summary>
    internal const long BoundaryBytes = 1024 * 1024;

    private readonly ITargetDiskDevice _device;
    private readonly DiskOperationRecorder _recorder;
    private readonly string _diskLabel;
    private IReadOnlyList<IVolumeLock>? _locks;
    private bool _offline;
    private bool _disposed;

    /// <summary>Takes ownership of <paramref name="device"/>. The factory disposes the device when this constructor throws.</summary>
    internal TargetDisk(ITargetDiskDevice device, DiskOperationRecorder recorder, string diskLabel)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(recorder);
        ArgumentException.ThrowIfNullOrWhiteSpace(diskLabel);

        if (device.LogicalSectorSize is not (512 or 4096))
        {
            throw new InvalidOperationException($"Unsupported logical sector size {device.LogicalSectorSize}.");
        }

        if (device.Length <= 0 || device.Length % device.LogicalSectorSize != 0)
        {
            throw new InvalidOperationException("The disk length is not a positive multiple of its sector size.");
        }

        _device = device;
        _recorder = recorder;
        _diskLabel = diskLabel;
    }

    /// <inheritdoc />
    public Task LockAndTakeOfflineAsync(CancellationToken ct) => Task.Run(() => LockAndTakeOffline(ct), ct);

    /// <inheritdoc />
    public Task WipeBoundariesAsync(CancellationToken ct) => Task.Run(() => WipeBoundaries(ct), ct);

    /// <inheritdoc />
    public ValueTask WriteAsync(long diskOffset, ReadOnlyMemory<byte> alignedBuffer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureOpen();
        RequireOffline("write");
        ValidateRange(diskOffset, alignedBuffer.Length);

        WriteDevice(diskOffset, alignedBuffer.Span);
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask<int> ReadAsync(long diskOffset, Memory<byte> alignedBuffer, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        EnsureOpen();
        ValidateRange(diskOffset, alignedBuffer.Length);

        int read;
        try
        {
            read = _device.Read(diskOffset, alignedBuffer.Span);
        }
        catch (Exception ex) when (ex is not PlatformException)
        {
            throw new PlatformException(
                PlatformErrorCode.TargetWriteError,
                $"Read of {alignedBuffer.Length} bytes at {diskOffset} failed.",
                ex);
        }

        if (read != alignedBuffer.Length)
        {
            throw new PlatformException(
                PlatformErrorCode.TargetWriteError,
                $"Short read at {diskOffset}: {read} of {alignedBuffer.Length} bytes.");
        }

        return ValueTask.FromResult(read);
    }

    /// <inheritdoc />
    public Task CommitLayoutAsync(TargetLayout layout, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(layout);
        return Task.Run(() => CommitLayout(layout, ct), ct);
    }

    /// <inheritdoc />
    public Task BringOnlineAsync(CancellationToken ct) => Task.Run(() => BringOnline(ct), ct);

    /// <inheritdoc />
    public Task DeleteLayoutAsync(CancellationToken ct) => Task.Run(() => DeleteLayout(ct), ct);

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        try
        {
            ReleaseLocks();

            if (_offline || IsStillOffline())
            {
                BringOnlineBestEffort();
            }
        }
        finally
        {
            _device.Dispose();
        }

        return ValueTask.CompletedTask;
    }

    private void LockAndTakeOffline(CancellationToken ct)
    {
        EnsureOpen();
        ct.ThrowIfCancellationRequested();

        if (_locks is not null || _offline)
        {
            throw new InvalidOperationException("The target disk is already locked or offline.");
        }

        Recorded("Lock", null, null, () => _locks = LockVolumes());

        try
        {
            Recorded("Offline", null, null, () =>
            {
                SetOfflineChecked();
                _offline = true;
            });
        }
        catch
        {
            // The disk did not go offline, so nothing is left in a state that needs the locks.
            ReleaseLocks();
            throw;
        }
    }

    private void WipeBoundaries(CancellationToken ct)
    {
        EnsureOpen();
        RequireOffline("wipe the boundaries");
        ct.ThrowIfCancellationRequested();

        long length = _device.Length;
        if (length < 2 * BoundaryBytes)
        {
            throw new InvalidOperationException("The target disk is smaller than 2 MiB.");
        }

        Recorded("Write", 0, BoundaryBytes, () => WriteZeros(0, BoundaryBytes));
        ct.ThrowIfCancellationRequested();
        Recorded("Write", length - BoundaryBytes, BoundaryBytes, () => WriteZeros(length - BoundaryBytes, BoundaryBytes));
    }

    private void CommitLayout(TargetLayout layout, CancellationToken ct)
    {
        EnsureOpen();
        RequireOffline("commit the layout");
        ct.ThrowIfCancellationRequested();

        int sector = _device.LogicalSectorSize;
        if (layout.DiskSize.Bytes != _device.Length)
        {
            throw new ArgumentException("The layout disk size does not match the target disk.", nameof(layout));
        }

        if (layout.LogicalSectorSize.Bytes != sector)
        {
            throw new ArgumentException("The layout sector size does not match the target disk.", nameof(layout));
        }

        GptDiskLayout gptLayout = new(
            layout.DiskGuid,
            layout.DiskSize.Bytes,
            sector,
            layout.Entries.Select(entry => ToGptEntry(entry, sector)).ToArray());

        GptImage image = GptSerializer.Serialize(gptLayout);

        long lastLba = _device.Length / sector - 1;
        long entrySectors = GptSerializer.EntryArrayBytes / sector;
        long backupEntriesOffset = (lastLba - entrySectors) * sector;
        long backupHeaderOffset = lastLba * sector;
        const long protectiveMbrOffset = 0;
        long primaryHeaderOffset = sector;
        long primaryEntriesOffset = 2L * sector;

        byte[]? backupEntries = DataAt(image.Writes, backupEntriesOffset);
        byte[]? backupHeader = DataAt(image.Writes, backupHeaderOffset);
        byte[]? primaryEntries = DataAt(image.Writes, primaryEntriesOffset);
        byte[]? protectiveMbr = DataAt(image.Writes, protectiveMbrOffset);
        byte[]? primaryHeader = DataAt(image.Writes, primaryHeaderOffset);
        if (image.Writes.Count != 5 || backupEntries is null || backupHeader is null ||
            primaryEntries is null || protectiveMbr is null || primaryHeader is null)
        {
            throw new InvalidOperationException("The serializer produced an unexpected GPT write set.");
        }

        // Commit order (R4, step 5). The primary header is last, so an interrupted commit never leaves a valid primary GPT.
        Recorded("CommitLayout", null, null, () =>
        {
            WriteRaw(backupEntriesOffset, backupEntries);
            WriteRaw(backupHeaderOffset, backupHeader);
            WriteRaw(primaryEntriesOffset, primaryEntries);
            WriteRaw(protectiveMbrOffset, protectiveMbr);
            WriteRaw(primaryHeaderOffset, primaryHeader);
        });
    }

    private void BringOnline(CancellationToken ct)
    {
        EnsureOpen();
        ct.ThrowIfCancellationRequested();

        Recorded("Online", null, null, () =>
        {
            try
            {
                _device.SetOffline(false);
                _offline = false;
                _device.UpdateProperties();
            }
            catch (Exception ex) when (ex is not PlatformException)
            {
                throw new PlatformException(PlatformErrorCode.Unknown, "Could not bring the target disk online.", ex);
            }
        });

        ReleaseLocks();
    }

    private void DeleteLayout(CancellationToken ct)
    {
        EnsureOpen();
        ct.ThrowIfCancellationRequested();

        // Works offline or online (R4). It does not check the offline state, because it is the compensation path.
        Recorded("DeleteLayout", null, null, () =>
        {
            long length = _device.Length;
            WriteZeros(0, BoundaryBytes);
            WriteZeros(length - BoundaryBytes, BoundaryBytes);

            if (!_offline)
            {
                try
                {
                    _device.UpdateProperties();
                }
                catch (Exception ex) when (ex is not PlatformException)
                {
                    throw new PlatformException(PlatformErrorCode.Unknown, "Could not refresh the target disk after deleting the layout.", ex);
                }
            }
        });
    }

    private IReadOnlyList<IVolumeLock> LockVolumes()
    {
        try
        {
            return _device.LockVolumesOnDisk();
        }
        catch (PlatformException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PlatformException(PlatformErrorCode.TargetInUse, "Could not lock the volumes of the target disk.", ex);
        }
    }

    private void SetOfflineChecked()
    {
        try
        {
            _device.SetOffline(true);
            if (!_device.IsOffline())
            {
                throw new PlatformException(
                    PlatformErrorCode.TargetInUse,
                    "The target disk did not report offline after IOCTL_DISK_SET_DISK_ATTRIBUTES.");
            }
        }
        catch (PlatformException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new PlatformException(PlatformErrorCode.TargetInUse, "Could not take the target disk offline.", ex);
        }
    }

    private bool IsStillOffline()
    {
        try
        {
            return _device.IsOffline();
        }
        catch (Exception)
        {
            // Unknown state: try to bring the disk online anyway. SetOffline(false) is idempotent.
            return true;
        }
    }

    private void BringOnlineBestEffort()
    {
        using DiskOperationScope scope = _recorder.Begin(_diskLabel, "Online");
        try
        {
            _device.SetOffline(false);
            _offline = false;
            _device.UpdateProperties();
            scope.Succeed();
        }
        catch (Exception ex)
        {
            // Dispose must not throw. The failure is recorded in the log, and Windows clears the non-persistent flag at reboot.
            scope.Fail(ex);
        }
    }

    private void ReleaseLocks()
    {
        IReadOnlyList<IVolumeLock>? locks = _locks;
        _locks = null;
        if (locks is null)
        {
            return;
        }

        foreach (IVolumeLock volumeLock in locks)
        {
            volumeLock.Dispose();
        }
    }

    private void WriteZeros(long offset, long length)
    {
        using AlignedBuffer buffer = new(checked((int)length), (nuint)_device.LogicalSectorSize);
        buffer.Span.Clear();
        WriteDevice(offset, buffer.Span);
    }

    private void WriteRaw(long offset, byte[] data)
    {
        using AlignedBuffer buffer = new(data.Length, (nuint)_device.LogicalSectorSize);
        data.AsSpan().CopyTo(buffer.Span);
        WriteDevice(offset, buffer.Span);
    }

    private void WriteDevice(long offset, ReadOnlySpan<byte> data)
    {
        try
        {
            _device.Write(offset, data);
        }
        catch (Exception ex) when (ex is not PlatformException)
        {
            throw new PlatformException(
                PlatformErrorCode.TargetWriteError,
                $"Write of {data.Length} bytes at {offset} failed.",
                ex);
        }
    }

    private void ValidateRange(long offset, long length)
    {
        int sector = _device.LogicalSectorSize;

        if (length <= 0 || offset < 0 || offset % sector != 0 || length % sector != 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                $"The offset and the length must be positive multiples of the sector size {sector}.");
        }

        if (length > _device.Length - offset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(offset),
                offset,
                $"The range {offset} to {offset + length} is outside the disk of {_device.Length} bytes.");
        }
    }

    private void Recorded(string operation, long? offset, long? length, Action body)
    {
        using DiskOperationScope scope = _recorder.Begin(_diskLabel, operation, offset, length);
        try
        {
            body();
            scope.Succeed();
        }
        catch (Exception ex)
        {
            scope.Fail(ex);
            throw;
        }
    }

    private void EnsureOpen() => ObjectDisposedException.ThrowIf(_disposed, this);

    private void RequireOffline(string action)
    {
        if (!_offline)
        {
            throw new InvalidOperationException($"Cannot {action} while the target disk is online.");
        }
    }

    private static byte[]? DataAt(IReadOnlyList<GptWrite> writes, long offset) =>
        writes.FirstOrDefault(write => write.Offset == offset)?.Data;

    private static GptPartitionEntry ToGptEntry(TargetLayoutEntry entry, int sector)
    {
        if (entry.Extent.Offset % sector != 0 || entry.Extent.Length % sector != 0)
        {
            throw new ArgumentException($"Partition extent {entry.Extent} is not aligned to the sector size {sector}.");
        }

        return new GptPartitionEntry(
            entry.Type.Value,
            entry.UniqueId.Value,
            entry.Extent.Offset / sector,
            (entry.Extent.Offset + entry.Extent.Length) / sector - 1,
            entry.Attributes,
            entry.Name);
    }
}
