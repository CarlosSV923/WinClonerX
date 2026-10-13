using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using Xunit;

namespace WinCopy.Application.Tests.Fakes;

public sealed class FakesSmokeTests
{
    [Fact]
    public async Task FaultInjectionThrowsConfiguredExceptionFromAsyncMethod()
    {
        var reader = new FakeDiskInventoryReader();
        reader.FailOn[nameof(IDiskInventoryReader.ReadAsync)] =
            new PlatformException(PlatformErrorCode.AccessDenied, "denied by test");

        var thrown = await Should.ThrowAsync<PlatformException>(() => reader.ReadAsync(CancellationToken.None));

        thrown.Code.ShouldBe(PlatformErrorCode.AccessDenied);
        thrown.TechnicalDetail.ShouldBe("denied by test");
        reader.Calls.ShouldBe(new[] { nameof(IDiskInventoryReader.ReadAsync) });
    }

    [Fact]
    public void FaultInjectionThrowsConfiguredExceptionFromSynchronousMethod()
    {
        var log = new FakeExecutionLog();
        log.FailOn[nameof(IExecutionLog.Record)] = new InvalidOperationException("log full");

        Should.Throw<InvalidOperationException>(() => log.Record(new LogEvent("Started", new Dictionary<string, object?>())));

        log.Events.ShouldBeEmpty();
        log.Calls.ShouldBe(new[] { nameof(IExecutionLog.Record) });
    }

    [Fact]
    public async Task CallLogRecordsEveryCallInOrder()
    {
        var log = new FakeExecutionLog();

        log.Record(new LogEvent("Started", new Dictionary<string, object?>()));
        await log.ExportAsync(@"C:\temp\out.zip", includeInventory: true, CancellationToken.None);

        log.Calls.ShouldBe(new[] { nameof(IExecutionLog.Record), nameof(IExecutionLog.ExportAsync) });
        log.Events.Count.ShouldBe(1);
        log.Exports.Count.ShouldBe(1);
        log.Exports[0].DestinationPath.ShouldBe(@"C:\temp\out.zip");
        log.Exports[0].IncludeInventory.ShouldBeTrue();
    }

    [Fact]
    public void PreventSleepBlocksUntilEveryHandleIsReleased()
    {
        var power = new FakePowerManager();

        var first = power.PreventSleep();
        var second = power.PreventSleep();
        power.IsSleepBlocked.ShouldBeTrue();
        power.Acquisitions.ShouldBe(2);

        first.Dispose();
        power.IsSleepBlocked.ShouldBeTrue();

        second.Dispose();
        second.Dispose();
        power.IsSleepBlocked.ShouldBeFalse();
        power.Releases.ShouldBe(2);
    }

    [Fact]
    public async Task DisposingSnapshotSetRecordsDeletion()
    {
        var service = new FakeSnapshotService();
        var set = await service.CreateAsync([new PartitionGuid(Guid.NewGuid())], CancellationToken.None);
        var created = service.CreatedSets.Single();
        created.Deleted.ShouldBeFalse();

        await set.DisposeAsync();

        created.Deleted.ShouldBeTrue();
        created.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task FailedDeletionIsStillRecordedAsAttempted()
    {
        var set = new FakeSnapshotSet(new Dictionary<PartitionGuid, FakeBlockSource>());
        set.FailOn[nameof(IAsyncDisposable.DisposeAsync)] = new IOException("volume busy");

        await Should.ThrowAsync<IOException>(async () => await set.DisposeAsync());

        set.Deleted.ShouldBeTrue();
        set.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task BlockSourceServesBytesFromBufferAndStopsAtEnd()
    {
        var source = new FakeBlockSource([1, 2, 3, 4]);
        var buffer = new byte[8];

        var read = await source.ReadAsync(2, buffer, CancellationToken.None);
        var atEnd = await source.ReadAsync(4, buffer, CancellationToken.None);
        var allocation = await source.GetAllocationAsync(CancellationToken.None);

        read.ShouldBe(2);
        buffer[0].ShouldBe((byte)3);
        buffer[1].ShouldBe((byte)4);
        atEnd.ShouldBe(0);
        source.Length.ShouldBe(new ByteSize(4));
        allocation.TotalUsed.ShouldBe(new ByteSize(4));
    }

    [Fact]
    public void RaiseDeliversChangesOnlyToActiveSubscribers()
    {
        var monitor = new FakeDeviceChangeMonitor();
        var received = new List<DeviceChange>();
        var subscription = monitor.Changes.Subscribe(new CollectingObserver(received));

        monitor.Raise(new DeviceChange(DeviceChangeKind.Removed, 2));
        subscription.Dispose();
        monitor.Raise(new DeviceChange(DeviceChangeKind.Arrived, 2));

        received.Count.ShouldBe(1);
        received[0].Kind.ShouldBe(DeviceChangeKind.Removed);
        monitor.SubscriberCount.ShouldBe(0);
    }

    [Fact]
    public async Task MountReleaseIsCountedWhenReleased()
    {
        var mounter = new FakeTemporaryMounter();
        var mount = await mounter.MountAsync(new PartitionGuid(Guid.NewGuid()), CancellationToken.None);
        mounter.ActiveMounts.ShouldBe(1);

        await mount.Release.DisposeAsync();

        mounter.ActiveMounts.ShouldBe(0);
        mounter.ReleaseCount.ShouldBe(1);
    }

    private sealed class CollectingObserver(List<DeviceChange> sink) : IObserver<DeviceChange>
    {
        public void OnNext(DeviceChange value) => sink.Add(value);

        public void OnError(Exception error) => throw new InvalidOperationException("Unexpected error", error);

        public void OnCompleted()
        {
        }
    }
}
