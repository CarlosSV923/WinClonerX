using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Logging;
using WinClonerX.Infrastructure.Windows.Storage;
using WinClonerX.Infrastructure.Windows.Tests.Fakes;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Storage;

// Every call to the Storage Management API goes through FakePartitionResizeApi. No test queries WMI or resizes anything.
public sealed class StorageSourceResizerTests
{
    private const long Mib = 1L << 20;
    private const long Gib = 1L << 30;

    private const string RefusalMessage = "refusing to resize a partition that is not the running Windows partition";

    private static readonly DateTimeOffset Now = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static readonly PartitionGuid Partition = new(new Guid("00000000-0000-0000-0000-0000000000A3"));

    private static readonly string WindowsVolume = StorageRows.VolumePathOf(new Guid("1C5367FB-D302-425A-9F83-460D2490DE24"));
    private static readonly string RecoveryVolume = StorageRows.VolumePathOf(new Guid("00000000-0000-0000-0000-0000000000E4"));
    private static readonly string OtherDiskVolume = StorageRows.VolumePathOf(new Guid("00000000-0000-0000-0000-0000000000F1"));

    private const string WindowsDevice = @"\\.\C:";

    private readonly FakePartitionResizeApi _api = new() { Info = WindowsPartition() };
    private readonly FakeVolumeExtentsReader _extents = WindowsExtents();
    private readonly RecordingExecutionLog _log = new();
    private readonly StorageSourceResizer _resizer;

    public StorageSourceResizerTests()
    {
        _resizer = new StorageSourceResizer(
            _api,
            _extents,
            new DiskOperationRecorder(_log, new FixedClock(Now)),
            new FixedClock(Now));
    }

    // ---- safety rule ----

    [Theory]
    [InlineData("minimum")]
    [InlineData("shrink")]
    [InlineData("restore")]
    public async Task EveryMethodRefusesThePartitionOnTheSystemDiskThatIsNotTheWindowsVolume(string operation)
    {
        // The Recovery partition is on disk 0, like Windows, but it does not carry the Windows drive root.
        _api.Info = RecoveryPartition();

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() => Call(operation));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        ex.TechnicalDetail.ShouldBe(RefusalMessage);
        _api.ResizeCalls.ShouldBeEmpty();
        _log.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task RefusesAPartitionOnAnotherDiskEvenWhenItCarriesTheWindowsDrive()
    {
        _api.Info = WindowsPartition() with
        {
            DiskNumber = 1,
            AccessPaths = [@"C:\", OtherDiskVolume],
        };

        await AssertRefusedWithoutResizing(() => _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None));
    }

    [Fact]
    public async Task RefusesAPartitionWhoseAccessPathsLackTheWindowsDrive()
    {
        _api.Info = WindowsPartition() with { AccessPaths = [WindowsVolume], DriveLetter = null };

        await AssertRefusedWithoutResizing(() => _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None));
    }

    [Fact]
    public async Task RefusesAPartitionWhoseDriveLetterIsNotTheWindowsLetter()
    {
        _api.Info = WindowsPartition() with { DriveLetter = 'D' };

        await AssertRefusedWithoutResizing(() => _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None));
    }

    [Fact]
    public async Task RefusesAPartitionWithoutAVolumeGuidPath()
    {
        _api.Info = WindowsPartition() with { AccessPaths = [@"C:\"] };

        await AssertRefusedWithoutResizing(() => _resizer.RestoreAsync(Partition, new ByteSize(100 * Gib), CancellationToken.None));
    }

    [Fact]
    public async Task AFailureToReadTheDiskExtentsFailsClosedWithoutResizing()
    {
        _extents.Extents.Remove(WindowsDevice);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        ex.TechnicalDetail.ShouldNotBe(RefusalMessage);
        ex.InnerException.ShouldNotBeNull();
        _api.ResizeCalls.ShouldBeEmpty();
    }

    // ---- minimum size ----

    [Fact]
    public async Task GetMinimumSizeReturnsTheSupportedMinimumOfTheWindowsPartition()
    {
        _api.SizeMin = 30 * Gib;

        ByteSize minimum = await _resizer.GetMinimumSizeAsync(Partition, CancellationToken.None);

        minimum.ShouldBe(new ByteSize(30 * Gib));
        _api.ResizeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMinimumSizeWrapsAWmiFailureAsShrinkFailed()
    {
        var inner = new UnauthorizedAccessException("denied by WMI");
        _api.GetSupportedSizeFailure = inner;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.GetMinimumSizeAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        ex.InnerException.ShouldBeSameAs(inner);
        ex.TechnicalDetail.ShouldContain("denied by WMI");
    }

    [Fact]
    public async Task GetMinimumSizeWrapsAFailureToReadThePartitionAsShrinkFailed()
    {
        _api.GetPartitionFailure = new InvalidOperationException("Expected exactly one MSFT_Partition");

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.GetMinimumSizeAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        ex.InnerException.ShouldBeOfType<InvalidOperationException>();
    }

    // ---- shrink ----

    // ---- maximum size ----

    [Fact]
    public async Task GetMaximumSizeReturnsTheSupportedMaximumOfTheWindowsPartitionWithoutResizing()
    {
        _api.SizeMax = 200 * Gib;

        ByteSize maximum = await _resizer.GetMaximumSizeAsync(Partition, CancellationToken.None);

        maximum.ShouldBe(new ByteSize(200 * Gib));
        _api.ResizeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetMaximumSizeRefusesThePartitionThatIsNotTheWindowsVolume()
    {
        _api.Info = RecoveryPartition();

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.GetMaximumSizeAsync(Partition, CancellationToken.None));

        ex.TechnicalDetail.ShouldBe(RefusalMessage);
        _api.ResizeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShrinkBelowTheSupportedMinimumIsRefusedWithoutResizing()
    {
        _api.SizeMin = 30 * Gib;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.ShrinkAsync(Partition, new ByteSize((30 * Gib) - 1), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        ex.TechnicalDetail.ShouldContain("cannot shrink below the supported minimum");
        _api.ResizeCalls.ShouldBeEmpty();
        _log.Events.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(100L)]
    [InlineData(120L)]
    public async Task ShrinkToTheCurrentSizeOrLargerIsRefusedWithoutResizing(long gib)
    {
        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.ShrinkAsync(Partition, new ByteSize(gib * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        _api.ResizeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task ShrinkCallsResizeOnceWithTheExactSizeAndRecordsTheOperation()
    {
        await _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None);

        _api.ResizeCalls.ShouldBe([(0, 3, 50 * Gib)]);

        LogEvent e = _log.Events.Single();
        e.Event.ShouldBe("DiskOperation");
        e.Level.ShouldBe(LogLevelKind.Information);
        e.Properties["Operation"].ShouldBe("Shrink");
        e.Properties["Result"].ShouldBe("Success");
        ((long)e.Properties["Offset"]!).ShouldBe(1 * Gib);
        ((long)e.Properties["Length"]!).ShouldBe(50 * Gib);
    }

    [Fact]
    public async Task ShrinkToleratesWindowsRoundingUpToTheNextMiB()
    {
        long requested = (50 * Gib) + 1;
        _api.SizeAfterResize = (50 * Gib) + Mib;

        await _resizer.ShrinkAsync(Partition, new ByteSize(requested), CancellationToken.None);

        _api.ResizeCalls.ShouldBe([(0, 3, requested)]);
        _log.Events.Single().Properties["Result"].ShouldBe("Success");
    }

    [Fact]
    public async Task ShrinkWhoseResultIsNotTheRequestedSizeFailsAndIsRecorded()
    {
        _api.SizeAfterResize = 49 * Gib;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        LogEvent e = _log.Events.Single();
        e.Level.ShouldBe(LogLevelKind.Error);
        e.Properties["Result"].ShouldBe("Failed");
    }

    [Fact]
    public async Task ShrinkWhoseResizeFailsIsWrappedAndRecorded()
    {
        var inner = new InvalidOperationException("MSFT_Partition.Resize returned 1");
        _api.ResizeFailure = inner;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        ex.InnerException.ShouldBeSameAs(inner);
        _log.Events.Single().Level.ShouldBe(LogLevelKind.Error);
    }

    [Fact]
    public async Task ShrinkRefusesACancelledRequestBeforeAnyWork()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), cts.Token));

        _api.ResizeCalls.ShouldBeEmpty();
    }

    // ---- restore ----

    [Fact]
    public async Task RestoreAboveTheSupportedMaximumIsRefusedWithoutResizing()
    {
        _api.Info = WindowsPartition() with { Size = 50 * Gib };
        _api.SizeMax = 120 * Gib;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.RestoreAsync(Partition, new ByteSize(121 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RestoreFailed);
        ex.TechnicalDetail.ShouldContain("exceeds the largest size");
        _api.ResizeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreBelowTheCurrentSizeIsRefusedWithoutResizing()
    {
        _api.Info = WindowsPartition() with { Size = 100 * Gib };

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.RestoreAsync(Partition, new ByteSize(90 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RestoreFailed);
        _api.ResizeCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreResizesOnceBackToTheOriginalLengthAndRecordsIt()
    {
        _api.Info = WindowsPartition() with { Size = 50 * Gib };
        _api.SizeMax = 120 * Gib;

        await _resizer.RestoreAsync(Partition, new ByteSize(100 * Gib), CancellationToken.None);

        _api.ResizeCalls.ShouldBe([(0, 3, 100 * Gib)]);

        LogEvent e = _log.Events.Single();
        e.Properties["Operation"].ShouldBe("Restore");
        e.Properties["Result"].ShouldBe("Success");
        ((long)e.Properties["Length"]!).ShouldBe(100 * Gib);
    }

    [Fact]
    public async Task RestoreAtTheOriginalLengthDoesNotResize()
    {
        _api.Info = WindowsPartition() with { Size = 100 * Gib };

        await _resizer.RestoreAsync(Partition, new ByteSize(100 * Gib), CancellationToken.None);

        _api.ResizeCalls.ShouldBeEmpty();
        _log.Events.ShouldBeEmpty();
    }

    [Fact]
    public async Task RestoreWhoseResultIsNotTheOriginalSizeFailsAndIsRecorded()
    {
        _api.Info = WindowsPartition() with { Size = 50 * Gib };
        _api.SizeMax = 120 * Gib;
        _api.SizeAfterResize = 99 * Gib;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.RestoreAsync(Partition, new ByteSize(100 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RestoreFailed);
        LogEvent e = _log.Events.Single();
        e.Level.ShouldBe(LogLevelKind.Error);
        e.Properties["Result"].ShouldBe("Failed");
    }

    [Fact]
    public async Task RestoreWhoseResizeFailsIsWrappedAsRestoreFailed()
    {
        _api.Info = WindowsPartition() with { Size = 50 * Gib };
        _api.SizeMax = 120 * Gib;
        var inner = new InvalidOperationException("MSFT_Partition.Resize returned 2");
        _api.ResizeFailure = inner;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.RestoreAsync(Partition, new ByteSize(100 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RestoreFailed);
        ex.InnerException.ShouldBeSameAs(inner);
    }

    [Fact]
    public async Task RestoreWhoseSupportedSizeQueryFailsIsWrappedAsRestoreFailed()
    {
        _api.GetSupportedSizeFailure = new UnauthorizedAccessException("denied");

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            _resizer.RestoreAsync(Partition, new ByteSize(100 * Gib), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RestoreFailed);
        _api.ResizeCalls.ShouldBeEmpty();
    }

    // ---- helpers ----

    private Task Call(string operation) => operation switch
    {
        "minimum" => _resizer.GetMinimumSizeAsync(Partition, CancellationToken.None),
        "shrink" => _resizer.ShrinkAsync(Partition, new ByteSize(50 * Gib), CancellationToken.None),
        _ => _resizer.RestoreAsync(Partition, new ByteSize(100 * Gib), CancellationToken.None),
    };

    private async Task AssertRefusedWithoutResizing(Func<Task> act)
    {
        PlatformException ex = await Should.ThrowAsync<PlatformException>(act);

        ex.Code.ShouldBe(PlatformErrorCode.ShrinkFailed);
        ex.TechnicalDetail.ShouldBe(RefusalMessage);
        _api.ResizeCalls.ShouldBeEmpty();
        _log.Events.ShouldBeEmpty();
    }

    /// <summary>The running Windows partition: disk 0, drive C:, a volume GUID path, 100 GiB long.</summary>
    private static PartitionResizeInfo WindowsPartition() => new(
        DiskNumber: 0,
        PartitionNumber: 3,
        Offset: 1 * Gib,
        Size: 100 * Gib,
        AccessPaths: [@"C:\", WindowsVolume],
        DriveLetter: 'C');

    /// <summary>The Recovery partition on the system disk. It has a volume GUID path but no drive letter.</summary>
    private static PartitionResizeInfo RecoveryPartition() => new(
        DiskNumber: 0,
        PartitionNumber: 4,
        Offset: 200 * Gib,
        Size: 1 * Gib,
        AccessPaths: [RecoveryVolume],
        DriveLetter: null);

    /// <summary>Windows is on disk 0. The Windows, Recovery and other-disk volumes each map to their own disk.</summary>
    private static FakeVolumeExtentsReader WindowsExtents()
    {
        var extents = new FakeVolumeExtentsReader { WindowsPath = WindowsDevice };
        extents.Extents[WindowsDevice] = new HashSet<uint> { 0 };
        extents.Extents[VolumePath.ToDevicePath(WindowsVolume)] = new HashSet<uint> { 0 };
        extents.Extents[VolumePath.ToDevicePath(RecoveryVolume)] = new HashSet<uint> { 0 };
        extents.Extents[VolumePath.ToDevicePath(OtherDiskVolume)] = new HashSet<uint> { 1 };
        return extents;
    }
}

/// <summary>
/// Scripted <see cref="IPartitionResizeApi"/>. A resize moves <see cref="Info"/> to <see cref="SizeAfterResize"/>, or to the
/// requested size when that is null. Each failure is thrown from its call.
/// </summary>
internal sealed class FakePartitionResizeApi : IPartitionResizeApi
{
    public PartitionResizeInfo Info { get; set; } = null!;

    public long SizeMin { get; set; } = 30L << 30;

    public long SizeMax { get; set; } = 120L << 30;

    public long? SizeAfterResize { get; set; }

    public Exception? GetPartitionFailure { get; set; }

    public Exception? GetSupportedSizeFailure { get; set; }

    public Exception? ResizeFailure { get; set; }

    public List<(int Disk, int Partition, long Size)> ResizeCalls { get; } = [];

    public PartitionResizeInfo GetPartition(Guid partitionGuid)
    {
        if (GetPartitionFailure is not null)
        {
            throw GetPartitionFailure;
        }

        return Info;
    }

    public (long SizeMin, long SizeMax) GetSupportedSize(int diskNumber, int partitionNumber)
    {
        if (GetSupportedSizeFailure is not null)
        {
            throw GetSupportedSizeFailure;
        }

        return (SizeMin, SizeMax);
    }

    public void Resize(int diskNumber, int partitionNumber, long newSize)
    {
        ResizeCalls.Add((diskNumber, partitionNumber, newSize));

        if (ResizeFailure is not null)
        {
            throw ResizeFailure;
        }

        Info = Info with { Size = SizeAfterResize ?? newSize };
    }
}
