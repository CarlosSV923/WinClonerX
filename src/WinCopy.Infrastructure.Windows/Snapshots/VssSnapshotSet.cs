using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;

namespace WinCopy.Infrastructure.Windows.Snapshots;

/// <summary>
/// The snapshot set of one run. Disposing it always deletes its snapshots, and it can be disposed more than once.
/// All VSS calls for the set run on the MTA worker thread that created it.
/// </summary>
/// <remarks>
/// T060 only creates and deletes the set. The volume sources arrive in T061, so <see cref="OpenVolume"/> is not
/// implemented yet.
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

    /// <summary>Not implemented yet. The SnapshotVolumeSource that T061 adds replaces this method.</summary>
    /// <exception cref="NotImplementedException">Always thrown until T061 is done.</exception>
    public IBlockSource OpenVolume(PartitionGuid partition) =>
        throw new NotImplementedException("SnapshotVolumeSource arrives in T061");

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
