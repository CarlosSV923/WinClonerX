using System.Threading;
using WinClonerX.Application.Errors;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Snapshots;
using WinClonerX.Infrastructure.Windows.Storage;

namespace WinClonerX.Infrastructure.Windows.Tests.Snapshots;

/// <summary>
/// Fake IVssSession. Records every call in order, with the apartment state and managed thread of the thread that ran
/// it. A step listed in <see cref="FailAt"/> throws its exception after it is recorded.
/// </summary>
internal sealed class FakeVssSession : IVssSession
{
    private readonly Dictionary<string, PlatformException> _failures = [];

    public Guid SnapshotSetId { get; } = Guid.NewGuid();

    public List<string> Calls { get; } = [];

    public List<ApartmentState> Apartments { get; } = [];

    public List<int> ThreadIds { get; } = [];

    public List<string> AddedVolumePaths { get; } = [];

    public List<Guid> SnapshotIds { get; } = [];

    public int DisposeCount { get; private set; }

    public void FailAt(string step, PlatformException failure) => _failures[step] = failure;

    public void InitializeForBackup() => Step("InitializeForBackup", "InitializeForBackup");

    public void SetContext(int context) => Step("SetContext", $"SetContext({context})");

    public void SetBackupState(bool selectComponents, bool backupBootableSystemState, int backupType, bool partialFileSupport) =>
        Step(
            "SetBackupState",
            $"SetBackupState({selectComponents},{backupBootableSystemState},{backupType},{partialFileSupport})");

    public void GatherWriterMetadata() => Step("GatherWriterMetadata", "GatherWriterMetadata");

    public Guid StartSnapshotSet()
    {
        Step("StartSnapshotSet", "StartSnapshotSet");
        return SnapshotSetId;
    }

    public Guid AddToSnapshotSet(string volumePath, Guid providerId)
    {
        AddedVolumePaths.Add(volumePath);
        Step("AddToSnapshotSet", $"AddToSnapshotSet({volumePath},{providerId})");

        Guid snapshotId = Guid.NewGuid();
        SnapshotIds.Add(snapshotId);
        return snapshotId;
    }

    public void PrepareForBackup() => Step("PrepareForBackup", "PrepareForBackup");

    public void DoSnapshotSet() => Step("DoSnapshotSet", "DoSnapshotSet");

    public string GetSnapshotDeviceObject(Guid snapshotId)
    {
        Step("GetSnapshotDeviceObject", $"GetSnapshotDeviceObject({snapshotId})");
        int index = SnapshotIds.IndexOf(snapshotId) + 1;
        return $@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy{index}";
    }

    public void AbortBackup() => Step("AbortBackup", "AbortBackup");

    public void DeleteSnapshots(Guid snapshotSetId, bool forceDelete) =>
        Step("DeleteSnapshots", $"DeleteSnapshots({snapshotSetId},{forceDelete})");

    public void BackupComplete() => Step("BackupComplete", "BackupComplete");

    public void Dispose()
    {
        DisposeCount++;
        Step("Dispose", "Dispose");
    }

    private void Step(string step, string call)
    {
        Calls.Add(call);
        Apartments.Add(Thread.CurrentThread.GetApartmentState());
        ThreadIds.Add(Environment.CurrentManagedThreadId);

        if (_failures.TryGetValue(step, out PlatformException? failure))
        {
            throw failure;
        }
    }
}

/// <summary>Fake factory that always hands out the same session and counts how often it was asked.</summary>
internal sealed class FakeVssBackupFactory(FakeVssSession session) : IVssBackupFactory
{
    public int CreateCount { get; private set; }

    public IVssSession Create()
    {
        CreateCount++;
        return session;
    }
}

/// <summary>Fake path resolver backed by a dictionary.</summary>
internal sealed class FakeVolumePathResolver : IVolumePathResolver
{
    public Dictionary<PartitionGuid, string> Paths { get; } = [];

    public string GetVolumeGuidPath(PartitionGuid partition) => Paths[partition];
}
