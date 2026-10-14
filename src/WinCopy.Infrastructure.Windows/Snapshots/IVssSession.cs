namespace WinCopy.Infrastructure.Windows.Snapshots;

/// <summary>
/// The VSS steps that the snapshot service needs, one method per step. Each method blocks until its step has
/// finished. A failing step throws <see cref="WinCopy.Application.Errors.PlatformException"/> with
/// <see cref="WinCopy.Application.Errors.PlatformErrorCode.SnapshotFailed"/>. The real implementation is
/// <see cref="ComVssSession"/>. Tests use a fake. Every method runs on the MTA worker thread.
/// </summary>
internal interface IVssSession : IDisposable
{
    /// <summary>Initializes the backup components with no XML document.</summary>
    void InitializeForBackup();

    /// <summary>Sets the VSS context, which is always the backup context here.</summary>
    void SetContext(int context);

    /// <summary>Sets the backup state. WinCopy uses copy backups with no component selection.</summary>
    void SetBackupState(bool selectComponents, bool backupBootableSystemState, int backupType, bool partialFileSupport);

    /// <summary>Gathers the writer metadata and waits for the operation to finish.</summary>
    void GatherWriterMetadata();

    /// <summary>Starts a snapshot set and returns its identifier.</summary>
    Guid StartSnapshotSet();

    /// <summary>Adds a volume to the current snapshot set and returns the identifier of its snapshot.</summary>
    Guid AddToSnapshotSet(string volumePath, Guid providerId);

    /// <summary>Prepares the writers for the backup and waits for the operation to finish.</summary>
    void PrepareForBackup();

    /// <summary>Creates the snapshots of the whole set and waits for the operation to finish.</summary>
    void DoSnapshotSet();

    /// <summary>Returns the device path of a snapshot, for example <c>\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopyN</c>.</summary>
    string GetSnapshotDeviceObject(Guid snapshotId);

    /// <summary>Aborts the backup in progress.</summary>
    void AbortBackup();

    /// <summary>Deletes every snapshot of the set. Pass <c>forceDelete</c> to delete even when VSS reports a conflict.</summary>
    void DeleteSnapshots(Guid snapshotSetId, bool forceDelete);

    /// <summary>Tells the writers that the backup has finished and waits for the operation to finish.</summary>
    void BackupComplete();
}
