using System.Threading;
using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Snapshots;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Snapshots;

// Every test runs the whole service against fakes. No VSS call reaches the operating system.
public sealed class VssSnapshotServiceTests : IDisposable
{
    private const string VolumePath = @"\\?\Volume{6a0a0b1c-1111-2222-3333-444455556666}\";

    private static readonly string[] ExpectedVolumePaths = [VolumePath];

    private static readonly PartitionGuid Partition1 = new(Guid.Parse("11111111-1111-1111-1111-111111111111"));
    private static readonly PartitionGuid Partition2 = new(Guid.Parse("22222222-2222-2222-2222-222222222222"));

    private readonly FakeVssSession _session = new();
    private readonly FakeVssBackupFactory _factory;
    private readonly FakeVolumePathResolver _resolver = new();
    private readonly List<string> _warnings = [];

    public VssSnapshotServiceTests()
    {
        _factory = new FakeVssBackupFactory(_session);
    }

    public void Dispose() => _session.Dispose();

    private VssSnapshotService CreateService() =>
        new(_resolver, _factory, message => _warnings.Add(message));

    private int CountCalls(string prefix) => _session.Calls.Count(call => call.StartsWith(prefix, StringComparison.Ordinal));

    private string DeleteCall => $"DeleteSnapshots({_session.SnapshotSetId},True)";

    [Fact]
    public async Task CreateThenDisposeCallsVssStepsInOrder()
    {
        _resolver.Paths[Partition1] = VolumePath;
        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);

        await set.DisposeAsync();

        string[] expected =
        [
            "InitializeForBackup",
            "SetContext(0)",
            "SetBackupState(False,False,5,False)",
            "GatherWriterMetadata",
            "StartSnapshotSet",
            $"AddToSnapshotSet({VolumePath},{Guid.Empty})",
            "PrepareForBackup",
            "DoSnapshotSet",
            $"GetSnapshotDeviceObject({_session.SnapshotIds[0]})",
            "BackupComplete",
            DeleteCall,
            "Dispose",
        ];
        _session.Calls.ShouldBe(expected);
    }

    [Fact]
    public async Task EveryVssCallRunsOnAnMtaThreadThatIsNotTheCaller()
    {
        _resolver.Paths[Partition1] = VolumePath;
        int callerThread = Environment.CurrentManagedThreadId;

        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);
        await set.DisposeAsync();

        _session.Apartments.ShouldNotBeEmpty();
        _session.Apartments.ShouldAllBe(state => state == ApartmentState.MTA);
        _session.ThreadIds.ShouldAllBe(id => id != callerThread);
        _session.ThreadIds.Distinct().Count().ShouldBe(1);
    }

    [Fact]
    public async Task VolumePathFromResolverKeepsItsTrailingBackslash()
    {
        _resolver.Paths[Partition1] = VolumePath;

        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);
        await set.DisposeAsync();

        _session.AddedVolumePaths.ShouldBe(ExpectedVolumePaths);
        _session.AddedVolumePaths[0].ShouldEndWith("\\");
    }

    [Fact]
    public async Task VolumePathWithoutTrailingBackslashGetsOne()
    {
        _resolver.Paths[Partition1] = VolumePath.TrimEnd('\\');

        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);
        await set.DisposeAsync();

        _session.AddedVolumePaths.ShouldBe(ExpectedVolumePaths);
    }

    [Fact]
    public async Task DevicePathsMapEachPartitionToItsSnapshotDevice()
    {
        _resolver.Paths[Partition1] = VolumePath;
        _resolver.Paths[Partition2] = @"\\?\Volume{7b7b7b7b-1111-2222-3333-444455556666}\";

        ISnapshotSet set = await CreateService().CreateAsync([Partition1, Partition2], CancellationToken.None);

        var devicePaths = ((VssSnapshotSet)set).DevicePaths;
        devicePaths[Partition1].ShouldBe(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy1");
        devicePaths[Partition2].ShouldBe(@"\\?\GLOBALROOT\Device\HarddiskVolumeShadowCopy2");

        await set.DisposeAsync();
    }

    [Fact]
    public async Task DoSnapshotSetFailureAbortsDeletesAndThrowsSnapshotFailed()
    {
        _resolver.Paths[Partition1] = VolumePath;
        _session.FailAt("DoSnapshotSet", new PlatformException(PlatformErrorCode.SnapshotFailed, "DoSnapshotSet failed: 0x80042302"));

        PlatformException error = await Should.ThrowAsync<PlatformException>(
            () => CreateService().CreateAsync([Partition1], CancellationToken.None));

        error.Code.ShouldBe(PlatformErrorCode.SnapshotFailed);
        _session.Calls.ShouldContain("AbortBackup");
        CountCalls("DeleteSnapshots").ShouldBe(1);
        _session.Calls.IndexOf("AbortBackup").ShouldBeLessThan(_session.Calls.IndexOf(DeleteCall));
        _session.Calls.ShouldContain(DeleteCall);
        _session.DisposeCount.ShouldBe(1);
        _session.Calls.ShouldNotContain("BackupComplete");
    }

    [Fact]
    public async Task FailureBeforeTheSetStartsReleasesSessionWithoutDeleting()
    {
        _resolver.Paths[Partition1] = VolumePath;
        _session.FailAt("GatherWriterMetadata", new PlatformException(PlatformErrorCode.SnapshotFailed, "GatherWriterMetadata failed: 0x80042301"));

        PlatformException error = await Should.ThrowAsync<PlatformException>(
            () => CreateService().CreateAsync([Partition1], CancellationToken.None));

        error.Code.ShouldBe(PlatformErrorCode.SnapshotFailed);
        CountCalls("DeleteSnapshots").ShouldBe(0);
        CountCalls("AbortBackup").ShouldBe(0);
        _session.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task FailureDuringCleanupIsLoggedAndTheOriginalErrorIsKept()
    {
        _resolver.Paths[Partition1] = VolumePath;
        _session.FailAt("DoSnapshotSet", new PlatformException(PlatformErrorCode.SnapshotFailed, "DoSnapshotSet failed: 0x80042302"));
        _session.FailAt("DeleteSnapshots", new PlatformException(PlatformErrorCode.SnapshotFailed, "DeleteSnapshots failed: 0x80042306"));

        PlatformException error = await Should.ThrowAsync<PlatformException>(
            () => CreateService().CreateAsync([Partition1], CancellationToken.None));

        error.TechnicalDetail.ShouldBe("DoSnapshotSet failed: 0x80042302");
        _warnings.ShouldHaveSingleItem();
        _session.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task CancellationBeforeStartTouchesNoVss()
    {
        _resolver.Paths[Partition1] = VolumePath;
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(
            () => CreateService().CreateAsync([Partition1], cancellation.Token));

        _factory.CreateCount.ShouldBe(0);
        _session.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task EmptyVolumeListIsRejected()
    {
        await Should.ThrowAsync<ArgumentException>(
            () => CreateService().CreateAsync([], CancellationToken.None));

        _factory.CreateCount.ShouldBe(0);
    }

    [Fact]
    public async Task DisposeTwiceDeletesTheSetExactlyOnce()
    {
        _resolver.Paths[Partition1] = VolumePath;
        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);

        await set.DisposeAsync();
        await set.DisposeAsync();

        CountCalls("DeleteSnapshots").ShouldBe(1);
        _session.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task ConcurrentDisposeDeletesTheSetExactlyOnce()
    {
        _resolver.Paths[Partition1] = VolumePath;
        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);

        await Task.WhenAll(set.DisposeAsync().AsTask(), set.DisposeAsync().AsTask());

        CountCalls("DeleteSnapshots").ShouldBe(1);
        _session.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task BackupCompleteFailureIsLoggedAndDeletionStillRuns()
    {
        _resolver.Paths[Partition1] = VolumePath;
        _session.FailAt("BackupComplete", new PlatformException(PlatformErrorCode.SnapshotFailed, "BackupComplete failed: 0x80042308"));
        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);

        await set.DisposeAsync();

        _warnings.ShouldHaveSingleItem();
        _session.Calls.ShouldContain(DeleteCall);
        _session.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task DeleteFailureThrowsSnapshotFailedAfterReleasingTheSession()
    {
        _resolver.Paths[Partition1] = VolumePath;
        _session.FailAt("DeleteSnapshots", new PlatformException(PlatformErrorCode.SnapshotFailed, "DeleteSnapshots failed: 0x80042306"));
        ISnapshotSet set = await CreateService().CreateAsync([Partition1], CancellationToken.None);

        PlatformException error = await Should.ThrowAsync<PlatformException>(() => set.DisposeAsync().AsTask());

        error.Code.ShouldBe(PlatformErrorCode.SnapshotFailed);
        _session.DisposeCount.ShouldBe(1);
    }
}
