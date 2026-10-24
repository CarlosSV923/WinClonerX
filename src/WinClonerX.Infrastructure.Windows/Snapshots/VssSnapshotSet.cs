using System.ComponentModel;
using System.Globalization;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.RawIo;

namespace WinClonerX.Infrastructure.Windows.Snapshots;

/// <summary>
/// The snapshot set of one run. Disposing it always deletes its snapshots, and it can be disposed more than once.
/// All VSS calls for the set run on the MTA worker thread that created it.
/// </summary>
/// <remarks>
/// <see cref="OpenVolume"/> opens the snapshot device of a partition as a read-only block source (T061).
/// </remarks>
internal sealed class VssSnapshotSet : ISnapshotSet
{
    private readonly Guid _snapshotSetId;
    private readonly IVssSession _session;
    private readonly MtaWorker _worker;
    private readonly Action<string>? _onWarning;
    private readonly object _disposeGate = new();
    private Task? _disposeTask;

    internal VssSnapshotSet(
        Guid snapshotSetId,
        IReadOnlyDictionary<PartitionGuid, string> devicePaths,
        IVssSession session,
        MtaWorker worker,
        Action<string>? onWarning)
    {
        _snapshotSetId = snapshotSetId;
        DevicePaths = devicePaths;
        _session = session;
        _worker = worker;
        _onWarning = onWarning;
    }

    /// <summary>Device path of each partition's snapshot, for example <c>\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN</c>.</summary>
    internal IReadOnlyDictionary<PartitionGuid, string> DevicePaths { get; }

    /// <summary>
    /// Opens the snapshot of <paramref name="partition"/> as a read-only block source. Each call opens a new device handle,
    /// and the caller disposes the source.
    /// </summary>
    /// <exception cref="KeyNotFoundException">The set has no snapshot for <paramref name="partition"/>.</exception>
    /// <exception cref="PlatformException">The snapshot device cannot be opened or read.</exception>
    public IBlockSource OpenVolume(PartitionGuid partition)
    {
        if (!DevicePaths.TryGetValue(partition, out string? devicePath))
        {
            throw new KeyNotFoundException(
                $"The snapshot set has no snapshot for the partition {partition.Value}.");
        }

        ISnapshotDevice device;
        try
        {
            device = new Win32SnapshotDevice(devicePath);
        }
        catch (Win32Exception ex)
        {
            PlatformErrorCode code = ex.NativeErrorCode switch
            {
                5 => PlatformErrorCode.AccessDenied,
                2 or 3 => PlatformErrorCode.DeviceNotFound,
                _ => PlatformErrorCode.SourceReadError,
            };
            throw new PlatformException(code, $"Could not open the snapshot device {devicePath}.", ex);
        }

        // The Read of the log names the snapshot of the partition: its offsets are relative to the start of the volume.
        return new LabeledBlockSource(
            new SnapshotVolumeSource(device),
            "snapshot of " + partition.Value.ToString("B", CultureInfo.InvariantCulture));
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Task task;
        lock (_disposeGate)
        {
            // Every call returns the same task, so the snapshots are deleted only once.
            _disposeTask ??= ReleaseAsync();
            task = _disposeTask;
        }

        return new ValueTask(task);
    }

    private async Task ReleaseAsync()
    {
        try
        {
            await _worker.RunAsync(ReleaseOnWorker).ConfigureAwait(false);
        }
        finally
        {
            _worker.Dispose();
        }
    }

    // Runs on the MTA thread. BackupComplete is best effort, and its failure is only logged. DeleteSnapshots is
    // required. The session is always released, even when the deletion fails, and the deletion failure is rethrown
    // after that.
    private void ReleaseOnWorker()
    {
        PlatformException? deleteFailure = null;
        try
        {
            try
            {
                _session.BackupComplete();
            }
            catch (PlatformException ex)
            {
                _onWarning?.Invoke($"BackupComplete failed: {ex.TechnicalDetail}");
            }

            try
            {
                _session.DeleteSnapshots(_snapshotSetId, true);
            }
            catch (PlatformException ex)
            {
                deleteFailure = ex;
            }
        }
        finally
        {
            _session.Dispose();
        }

        if (deleteFailure is not null)
        {
            throw deleteFailure;
        }
    }
}
