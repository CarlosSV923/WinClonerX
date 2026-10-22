using Shouldly;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Infrastructure.Windows.Platform;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Platform;

// The probe is a fake: no disk is opened and no device notification is registered.
public sealed class DiskRemovalTrackerTests
{
    [Fact]
    public void ReportsAWatchedDiskThatCanNoLongerBeOpenedOnce()
    {
        var probe = new FakeProbe { [2] = 2 };
        var reported = new List<DeviceChange>();
        var tracker = new DiskRemovalTracker([2], probe, reported.Add);

        tracker.Check();
        tracker.Check();

        reported.ShouldBe([new DeviceChange(DeviceChangeKind.Removed, 2)]);
    }

    [Fact]
    public void DoesNotReportADiskThatIsStillPresent()
    {
        var probe = new FakeProbe();
        var reported = new List<DeviceChange>();
        var tracker = new DiskRemovalTracker([2, 3], probe, reported.Add);

        tracker.Check();

        reported.ShouldBeEmpty();
        probe.Asked.ShouldBe([2, 3]);
    }

    [Fact]
    public void AnAccessErrorDoesNotMeanThatTheDiskIsGone()
    {
        var probe = new FakeProbe { [2] = 5 };
        var reported = new List<DeviceChange>();
        var tracker = new DiskRemovalTracker([2], probe, reported.Add);

        tracker.Check();

        reported.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(2)]    // ERROR_FILE_NOT_FOUND: la ruta del disco ya no existe
    [InlineData(3)]    // ERROR_PATH_NOT_FOUND
    [InlineData(21)]   // ERROR_NOT_READY
    [InlineData(433)]  // ERROR_NO_SUCH_DEVICE
    [InlineData(1167)] // ERROR_DEVICE_NOT_CONNECTED
    public void EachAbsenceCodeCountsAsRemoval(int nativeError)
    {
        var probe = new FakeProbe { [4] = nativeError };
        var reported = new List<DeviceChange>();
        var tracker = new DiskRemovalTracker([4], probe, reported.Add);

        tracker.Check();

        reported.ShouldBe([new DeviceChange(DeviceChangeKind.Removed, 4)]);
    }

    [Fact]
    public void OnlyTheWatchedDisksAreProbed()
    {
        var probe = new FakeProbe { [9] = 2 };
        var reported = new List<DeviceChange>();
        var tracker = new DiskRemovalTracker([1, 1, 2], probe, reported.Add);

        tracker.Check();

        probe.Asked.ShouldBe([1, 2]);
        reported.ShouldBeEmpty();
    }

    [Fact]
    public void ReportsEachRemovedDiskSeparately()
    {
        var probe = new FakeProbe { [1] = 2, [2] = 1167 };
        var reported = new List<DeviceChange>();
        var tracker = new DiskRemovalTracker([1, 2], probe, reported.Add);

        tracker.Check();

        reported.ShouldBe([
            new DeviceChange(DeviceChangeKind.Removed, 1),
            new DeviceChange(DeviceChangeKind.Removed, 2),
        ]);
    }

    [Fact]
    public void StopEndsReportingForGood()
    {
        var probe = new FakeProbe { [2] = 2 };
        var reported = new List<DeviceChange>();
        var tracker = new DiskRemovalTracker([2], probe, reported.Add);

        tracker.Stop();
        tracker.Check();

        reported.ShouldBeEmpty();
        probe.Asked.ShouldBeEmpty();
    }

    [Fact]
    public void ANullDiskListIsRejected()
    {
        Should.Throw<ArgumentNullException>(() => new DiskRemovalTracker(null!, new FakeProbe(), _ => { }));
    }

    /// <summary>Probe falso: un disco sin entrada está presente; con entrada, el valor es el código de error de apertura.</summary>
    private sealed class FakeProbe : IDiskPresenceProbe
    {
        private readonly Dictionary<int, int> _errors = [];

        public int this[int diskNumber]
        {
            get => _errors[diskNumber];
            set => _errors[diskNumber] = value;
        }

        public List<int> Asked { get; } = [];

        public int? OpenError(int diskNumber)
        {
            Asked.Add(diskNumber);
            return _errors.TryGetValue(diskNumber, out int error) ? error : null;
        }
    }
}
