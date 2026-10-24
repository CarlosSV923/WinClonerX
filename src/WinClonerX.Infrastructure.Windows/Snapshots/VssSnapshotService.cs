using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Interop.Vss;
using WinClonerX.Infrastructure.Windows.Storage;

namespace WinClonerX.Infrastructure.Windows.Snapshots;

/// <summary>
/// Creates one coherent VSS snapshot set for the NTFS volumes of the source disk (R1). The copy backup type means
/// the backup history of other applications does not change. All VSS work runs on a dedicated MTA thread, and that
/// thread stays alive until the returned snapshot set is disposed.
/// </summary>
internal sealed class VssSnapshotService(
    IVolumePathResolver volumePathResolver,
    IVssBackupFactory backupFactory,
    Action<string>? onWarning = null) : ISnapshotService
{
    /// <inheritdoc />
    public async Task<ISnapshotSet> CreateAsync(IReadOnlyList<PartitionGuid> ntfsVolumes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ntfsVolumes);
        if (ntfsVolumes.Count == 0)
        {
            throw new ArgumentException("At least one volume is required.", nameof(ntfsVolumes));
        }

        ct.ThrowIfCancellationRequested();

        // VSS expects the volume GUID path with its trailing backslash, for example \\?\Volume{...}\.
        VolumeTarget[] targets = ntfsVolumes
            .Select(partition => new VolumeTarget(partition, WithTrailingBackslash(volumePathResolver.GetVolumeGuidPath(partition))))
            .ToArray();

        var worker = new MtaWorker();
        try
        {
            return await worker.RunAsync<ISnapshotSet>(() => CreateOnWorker(targets, worker, ct)).ConfigureAwait(false);
        }
        catch
        {
            // The snapshot set never took ownership of the worker, so this method has to stop the thread.
            worker.Dispose();
            throw;
        }
    }

    private VssSnapshotSet CreateOnWorker(IReadOnlyList<VolumeTarget> targets, MtaWorker worker, CancellationToken ct)
    {
        IVssSession session = backupFactory.Create();
        Guid snapshotSetId = Guid.Empty;
        bool setStarted = false;

        try
        {
            session.InitializeForBackup();
            session.SetContext(VssConstants.VSS_CTX_BACKUP);
            session.SetBackupState(false, false, VssConstants.VSS_BT_COPY, false);
            session.GatherWriterMetadata();
            ct.ThrowIfCancellationRequested();

            snapshotSetId = session.StartSnapshotSet();
            setStarted = true;

            var snapshots = new List<(PartitionGuid Partition, Guid SnapshotId)>(targets.Count);
            foreach (VolumeTarget target in targets)
            {
                ct.ThrowIfCancellationRequested();
                snapshots.Add((target.Partition, session.AddToSnapshotSet(target.VolumePath, Guid.Empty)));
            }

            session.PrepareForBackup();
            ct.ThrowIfCancellationRequested();
            session.DoSnapshotSet();

            var devicePaths = new Dictionary<PartitionGuid, string>(snapshots.Count);
            foreach ((PartitionGuid partition, Guid snapshotId) in snapshots)
            {
                devicePaths[partition] = session.GetSnapshotDeviceObject(snapshotId);
            }

            return new VssSnapshotSet(snapshotSetId, devicePaths, session, worker, onWarning);
        }
        catch
        {
            // Once the set has started, a failure must not leave snapshots behind. Then release the session.
            if (setStarted)
            {
                AbortAndDelete(session, snapshotSetId);
            }

            session.Dispose();
            throw;
        }
    }

    private void AbortAndDelete(IVssSession session, Guid snapshotSetId)
    {
        TryCleanupStep("AbortBackup", session.AbortBackup);
        TryCleanupStep("DeleteSnapshots", () => session.DeleteSnapshots(snapshotSetId, true));
    }

    // A cleanup failure must not hide the original failure, so it is only reported to the warning callback.
    private void TryCleanupStep(string step, Action action)
    {
        try
        {
            action();
        }
        catch (PlatformException ex)
        {
            onWarning?.Invoke($"Cleanup {step} failed: {ex.TechnicalDetail}");
        }
    }

    private static string WithTrailingBackslash(string volumePath) =>
        volumePath.EndsWith('\\') ? volumePath : volumePath + '\\';

    private sealed record VolumeTarget(PartitionGuid Partition, string VolumePath);
}
