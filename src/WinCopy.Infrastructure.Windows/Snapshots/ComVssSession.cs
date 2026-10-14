using System.Runtime.InteropServices;
using WinCopy.Application.Errors;
using WinCopy.Infrastructure.Windows.Interop.Vss;

namespace WinCopy.Infrastructure.Windows.Snapshots;

/// <summary>
/// Real VSS session over a native IVssBackupComponents object. Every step checks its HRESULT and throws
/// <see cref="PlatformException"/> with <see cref="PlatformErrorCode.SnapshotFailed"/> when the call fails.
/// Must only be used on the MTA worker thread that created it.
/// </summary>
/// <remarks>
/// Ownership of native pointers. Each pointer that VSS returns through an <c>out</c> parameter arrives with one
/// reference owned by WinCopy. <see cref="NativeComPointer.ConvertAndFree{T}"/> converts the pointer into a managed
/// proxy and then releases that reference. The proxy holds its own references, so the native object stays
/// alive while it does. Dispose drops the managed proxy (the backup components), and the object is released
/// when the garbage collector collects the proxy.
/// VssSnapshotSet calls Dispose as its last step, after DeleteSnapshots.
/// </remarks>
internal sealed class ComVssSession : IVssSession
{
    private IVssBackupComponents? _components;

    internal ComVssSession(nint backupComponents)
    {
        _components = NativeComPointer.ConvertAndFree<IVssBackupComponents>(backupComponents)
            ?? throw new PlatformException(PlatformErrorCode.SnapshotFailed, "IVssBackupComponents wrapper is null");
    }

    private IVssBackupComponents Components =>
        _components ?? throw new ObjectDisposedException(nameof(ComVssSession));

    /// <inheritdoc />
    public void InitializeForBackup() =>
        Check(Components.InitializeForBackup(0), "InitializeForBackup");

    /// <inheritdoc />
    public void SetContext(int context) =>
        Check(Components.SetContext(context), "SetContext");

    /// <inheritdoc />
    public void SetBackupState(bool selectComponents, bool backupBootableSystemState, int backupType, bool partialFileSupport) =>
        Check(
            Components.SetBackupState(selectComponents, backupBootableSystemState, backupType, partialFileSupport),
            "SetBackupState");

    /// <inheritdoc />
    public void GatherWriterMetadata()
    {
        int hr = Components.GatherWriterMetadata(out nint asyncPointer);
        Check(hr, "GatherWriterMetadata");
        WaitForAsync(asyncPointer, "GatherWriterMetadata");
    }

    /// <inheritdoc />
    public Guid StartSnapshotSet()
    {
        int hr = Components.StartSnapshotSet(out Guid snapshotSetId);
        Check(hr, "StartSnapshotSet");
        return snapshotSetId;
    }

    /// <inheritdoc />
    public Guid AddToSnapshotSet(string volumePath, Guid providerId)
    {
        int hr = Components.AddToSnapshotSet(volumePath, providerId, out Guid snapshotId);
        Check(hr, "AddToSnapshotSet");
        return snapshotId;
    }

    /// <inheritdoc />
    public void PrepareForBackup()
    {
        int hr = Components.PrepareForBackup(out nint asyncPointer);
        Check(hr, "PrepareForBackup");
        WaitForAsync(asyncPointer, "PrepareForBackup");
    }

    /// <inheritdoc />
    public void DoSnapshotSet()
    {
        int hr = Components.DoSnapshotSet(out nint asyncPointer);
        Check(hr, "DoSnapshotSet");
        WaitForAsync(asyncPointer, "DoSnapshotSet");
    }

    /// <inheritdoc />
    public string GetSnapshotDeviceObject(Guid snapshotId)
    {
        int hr = Components.GetSnapshotProperties(snapshotId, out VSS_SNAPSHOT_PROP properties);
        Check(hr, "GetSnapshotProperties");

        // VSS allocates the strings inside the structure. They are freed in the finally block, even on failure.
        try
        {
            if (properties.SnapshotDeviceObject == 0)
            {
                throw new PlatformException(
                    PlatformErrorCode.SnapshotFailed,
                    "GetSnapshotProperties returned no device path");
            }

            string? devicePath = Marshal.PtrToStringUni(properties.SnapshotDeviceObject);
            if (string.IsNullOrEmpty(devicePath))
            {
                throw new PlatformException(
                    PlatformErrorCode.SnapshotFailed,
                    "GetSnapshotProperties returned an empty device path");
            }

            return devicePath;
        }
        finally
        {
            VssApi.VssFreeSnapshotProperties(ref properties);
        }
    }

    /// <inheritdoc />
    public void AbortBackup() =>
        Check(Components.AbortBackup(), "AbortBackup");

    /// <inheritdoc />
    public void DeleteSnapshots(Guid snapshotSetId, bool forceDelete)
    {
        int hr = Components.DeleteSnapshots(
            snapshotSetId,
            VssConstants.VSS_OBJECT_SNAPSHOT_SET,
            forceDelete,
            out _,
            out Guid notDeletedSnapshotId);
        Check(hr, "DeleteSnapshots");

        // VSS reports the first snapshot it could not delete. GUID_NULL means that every snapshot was deleted.
        if (notDeletedSnapshotId != Guid.Empty)
        {
            throw new PlatformException(
                PlatformErrorCode.SnapshotFailed,
                $"DeleteSnapshots left snapshot {notDeletedSnapshotId} undeleted");
        }
    }

    /// <inheritdoc />
    public void BackupComplete()
    {
        int hr = Components.BackupComplete(out nint asyncPointer);
        Check(hr, "BackupComplete");
        WaitForAsync(asyncPointer, "BackupComplete");
    }

    /// <inheritdoc />
    public void Dispose()
    {
        // Dropping the managed proxy lets the collector release the native backup components. See the class remarks.
        _components = null;
    }

    private static void Check(int hr, string step)
    {
        if (hr < 0)
        {
            throw Failed(step, hr);
        }
    }

    /// <summary>
    /// Waits for an IVssAsync operation to finish. Anything other than VSS_S_ASYNC_FINISHED is a failure.
    /// The pointer is converted and released at once; the proxy keeps the operation alive for the wait.
    /// </summary>
    private static void WaitForAsync(nint asyncPointer, string step)
    {
        if (asyncPointer == 0)
        {
            throw new PlatformException(PlatformErrorCode.SnapshotFailed, $"{step} returned no async object");
        }

        IVssAsync async = NativeComPointer.ConvertAndFree<IVssAsync>(asyncPointer)
            ?? throw new PlatformException(PlatformErrorCode.SnapshotFailed, $"{step} returned an invalid async object");

        Check(async.Wait(VssConstants.INFINITE), step);

        int callResult = async.QueryStatus(out int operationResult, 0);
        Check(callResult, step);
        if (operationResult != VssConstants.VSS_S_ASYNC_FINISHED)
        {
            throw Failed(step, operationResult);
        }
    }

    private static PlatformException Failed(string step, int hr) =>
        new(PlatformErrorCode.SnapshotFailed, $"{step} failed: 0x{hr:X8}");
}
