using System.Buffers.Binary;
using System.ComponentModel;
using System.Text;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports;
using WinClonerX.Domain.Planning;
using WinClonerX.Domain.Shared;
using WinClonerX.Infrastructure.Windows.Logging;
using WinClonerX.Infrastructure.Windows.RawIo;
using WinClonerX.Infrastructure.Windows.Tests.Fakes;
using WinClonerX.Infrastructure.Windows.Tests.Logging;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.RawIo;

// Every test runs against FakeTargetDiskDevice. No disk, volume or handle is touched.
public sealed class TargetDiskTests
{
    private const long Mib = Sizes.Mib;
    private const long DiskLength = 256 * Sizes.Mib;
    private const int EntryArrayBytes = 16384;
    private const string VolumeA = @"\\?\Volume{A}";
    private const string VolumeB = @"\\?\Volume{B}";
    private static readonly Guid DiskGuid = new("0F1E2D3C-4B5A-4968-8776-A5B4C3D2E1F0");
    private static readonly Guid PartitionGuid = new("11223344-5566-4778-8899-AABBCCDDEEFF");

    [Fact]
    public async Task WriteIsRefusedWhileTheDiskIsOnline()
    {
        await using var fx = new Fixture();

        await Should.ThrowAsync<InvalidOperationException>(async () =>
            await fx.Disk.WriteAsync(Mib, new byte[4096], CancellationToken.None));

        fx.Device.Writes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(-512L, 512)]
    [InlineData(DiskLength, 512)]
    [InlineData(DiskLength - 512, 1024)]
    public async Task OutOfBoundsWriteIsRefused(long offset, int length)
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await fx.Disk.WriteAsync(offset, new byte[length], CancellationToken.None));

        fx.Device.Writes.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(100L, 512)]
    [InlineData(512L, 100)]
    [InlineData(512L, 0)]
    public async Task MisalignedOrEmptyWriteIsRefused(long offset, int length)
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await fx.Disk.WriteAsync(offset, new byte[length], CancellationToken.None));

        fx.Device.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task WriteThatIsAlignedToTheSectorButNotTo4KnIsRefusedOn4KnDisks()
    {
        await using var fx = new Fixture(sectorSize: 4096);
        await fx.GoOfflineAsync();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await fx.Disk.WriteAsync(512, new byte[4096], CancellationToken.None));

        fx.Device.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task WriteWhileOfflineReachesTheDeviceWithItsBytes()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();
        byte[] data = Enumerable.Repeat((byte)0xAB, 4096).ToArray();

        await fx.Disk.WriteAsync(Mib, data, CancellationToken.None);

        fx.Device.Writes.Count.ShouldBe(1);
        fx.Device.Writes[0].Offset.ShouldBe(Mib);
        fx.Device.Writes[0].Data.ShouldBe(data);
    }

    [Fact]
    public async Task DeviceWriteFailureIsTargetWriteError()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();
        fx.Device.FailWriteAt = Mib;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(async () =>
            await fx.Disk.WriteAsync(Mib, new byte[4096], CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.TargetWriteError);
    }

    [Theory]
    [InlineData(1167, PlatformErrorCode.TargetDeviceRemoved)]
    [InlineData(1006, PlatformErrorCode.TargetDeviceRemoved)]
    [InlineData(29, PlatformErrorCode.TargetWriteError)]
    [InlineData(1117, PlatformErrorCode.TargetWriteError)]
    public async Task DeviceWriteFailureIsClassifiedByItsWin32Code(int nativeError, PlatformErrorCode expected)
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();
        fx.Device.FailWriteAt = Mib;
        fx.Device.WriteFailure = new Win32Exception(nativeError);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(async () =>
            await fx.Disk.WriteAsync(Mib, new byte[4096], CancellationToken.None));

        ex.Code.ShouldBe(expected);
    }

    [Fact]
    public async Task ReadReturnsTheBytesReadWithinTheDisk()
    {
        await using var fx = new Fixture();

        int read = await fx.Disk.ReadAsync(0, new byte[4096], CancellationToken.None);

        read.ShouldBe(4096);
    }

    [Fact]
    public async Task ReadOutsideTheDiskIsRefused()
    {
        await using var fx = new Fixture();

        await Should.ThrowAsync<ArgumentOutOfRangeException>(async () =>
            await fx.Disk.ReadAsync(DiskLength, new byte[512], CancellationToken.None));
    }

    [Fact]
    public async Task WipeWritesZerosToTheFirstAndTheLastMiB()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();

        await fx.Disk.WipeBoundariesAsync(CancellationToken.None);

        fx.Device.Writes.Select(w => (w.Offset, w.Data.Length)).ToArray()
            .ShouldBe(new[] { (0L, (int)Mib), (DiskLength - Mib, (int)Mib) });
        fx.Device.Writes.All(w => w.Data.All(b => b == 0)).ShouldBeTrue();
    }

    [Fact]
    public async Task WipeIsRefusedWhileTheDiskIsOnline()
    {
        await using var fx = new Fixture();

        await Should.ThrowAsync<InvalidOperationException>(() => fx.Disk.WipeBoundariesAsync(CancellationToken.None));

        fx.Device.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task LockAndOfflineRunInOrderAndRecordTheirOperations()
    {
        await using var fx = new Fixture();

        await fx.GoOfflineAsync();

        fx.Device.Calls.ShouldBe(["LockVolumes", "SetOffline(True)"]);
        fx.Device.Offline.ShouldBeTrue();
        fx.OperationNames.ShouldBe(["Lock", "Offline"]);
    }

    [Fact]
    public async Task VolumeLockFailureIsTargetInUseAndTheDiskStaysOnline()
    {
        await using var fx = new Fixture();
        fx.Device.LockFailure = new InvalidOperationException("volume busy");

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            fx.Disk.LockAndTakeOfflineAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.TargetInUse);
        fx.Device.Calls.ShouldNotContain("SetOffline(True)");
        fx.Device.Offline.ShouldBeFalse();
    }

    [Fact]
    public async Task DiskThatDoesNotReportOfflineIsTargetInUseAndTheLocksAreReleased()
    {
        await using var fx = new Fixture();
        fx.Device.IgnoreSetOffline = true;

        PlatformException ex = await Should.ThrowAsync<PlatformException>(() =>
            fx.Disk.LockAndTakeOfflineAsync(CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.TargetInUse);
        fx.Device.Calls.Where(c => c.StartsWith("Release(", StringComparison.Ordinal)).Count().ShouldBe(2);
    }

    [Fact]
    public async Task LockingTwiceIsRefused()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();

        await Should.ThrowAsync<InvalidOperationException>(() => fx.GoOfflineAsync());
    }

    [Theory]
    [InlineData(512)]
    [InlineData(4096)]
    public async Task CommitLayoutWritesTheBackupFirstAndThePrimaryHeaderLast(int sector)
    {
        await using var fx = new Fixture(sectorSize: sector);
        await fx.GoOfflineAsync();
        fx.Device.Calls.Clear();

        await fx.Disk.CommitLayoutAsync(Layout(sector), CancellationToken.None);

        long lastLba = DiskLength / sector - 1;
        long entrySectors = EntryArrayBytes / sector;
        fx.Device.Writes.Select(w => (w.Offset, w.Data.Length)).ToArray().ShouldBe(new[]
        {
            ((lastLba - entrySectors) * sector, EntryArrayBytes),   // 1. backup entries
            (lastLba * sector, sector),                             // 2. backup header
            (2L * sector, EntryArrayBytes),                         // 3. primary entries
            (0L, sector),                                           // 4. protective MBR
            ((long)sector, sector),                                 // 5. primary header, last
        });
        fx.OperationNames.ShouldBe(["Lock", "Offline", "CommitLayout"]);
    }

    [Fact]
    public async Task CommitLayoutWritesAValidProtectiveMbrAndGptHeaders()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();

        await fx.Disk.CommitLayoutAsync(Layout(), CancellationToken.None);

        const int sector = 512;
        long lastLba = DiskLength / sector - 1;
        byte[] backupHeader = fx.Device.Writes[1].Data;
        byte[] primaryEntries = fx.Device.Writes[2].Data;
        byte[] mbr = fx.Device.Writes[3].Data;
        byte[] primaryHeader = fx.Device.Writes[4].Data;

        mbr[510].ShouldBe((byte)0x55);
        mbr[511].ShouldBe((byte)0xAA);
        Encoding.ASCII.GetString(primaryHeader, 0, 8).ShouldBe("EFI PART");
        Encoding.ASCII.GetString(backupHeader, 0, 8).ShouldBe("EFI PART");
        BinaryPrimitives.ReadInt64LittleEndian(primaryHeader.AsSpan(24)).ShouldBe(1L);
        BinaryPrimitives.ReadInt64LittleEndian(primaryHeader.AsSpan(32)).ShouldBe(lastLba);
        BinaryPrimitives.ReadInt64LittleEndian(backupHeader.AsSpan(24)).ShouldBe(lastLba);
        BinaryPrimitives.ReadInt64LittleEndian(backupHeader.AsSpan(32)).ShouldBe(1L);
        BinaryPrimitives.ReadInt64LittleEndian(primaryEntries.AsSpan(32)).ShouldBe(Mib / sector);
        BinaryPrimitives.ReadInt64LittleEndian(primaryEntries.AsSpan(40)).ShouldBe((Mib + 64 * Mib) / sector - 1);
    }

    [Fact]
    public async Task CommitLayoutIsRefusedWhileTheDiskIsOnline()
    {
        await using var fx = new Fixture();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            fx.Disk.CommitLayoutAsync(Layout(), CancellationToken.None));

        fx.Device.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task CommitLayoutWithAnotherDiskSizeIsRejected()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();
        TargetLayout wrongSize = new(DiskGuid, ByteSize.FromMiB(128), new SectorSize(512), Layout().Entries);

        await Should.ThrowAsync<ArgumentException>(() => fx.Disk.CommitLayoutAsync(wrongSize, CancellationToken.None));

        fx.Device.Writes.ShouldBeEmpty();
    }

    [Fact]
    public async Task BringOnlineSetsOnlineRefreshesThenReleasesTheLocks()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();
        fx.Device.Calls.Clear();

        await fx.Disk.BringOnlineAsync(CancellationToken.None);

        fx.Device.Calls.ShouldBe(["SetOffline(False)", "UpdateProperties", $"Release({VolumeA})", $"Release({VolumeB})"]);
        fx.Device.Offline.ShouldBeFalse();
        fx.OperationNames.ShouldBe(["Lock", "Offline", "Online"]);
    }

    [Fact]
    public async Task DeleteLayoutWhileOfflineZerosBothEndsWithoutRefreshingTheDisk()
    {
        await using var fx = new Fixture();
        await fx.GoOfflineAsync();
        fx.Device.Calls.Clear();

        await fx.Disk.DeleteLayoutAsync(CancellationToken.None);

        fx.Device.Writes.Select(w => (w.Offset, w.Data.Length)).ToArray()
            .ShouldBe(new[] { (DiskLength - Mib, (int)Mib), (0L, (int)Mib) });
        fx.Device.Writes.All(w => w.Data.All(b => b == 0)).ShouldBeTrue();
        fx.Device.Calls.ShouldNotContain("UpdateProperties");
    }

    [Fact]
    public async Task DeleteLayoutWhileOnlineZerosBothEndsTailFirstAndRefreshesTheDisk()
    {
        await using var fx = new Fixture();

        await fx.Disk.DeleteLayoutAsync(CancellationToken.None);

        fx.Device.Calls.ShouldBe([$"Write({DiskLength - Mib},{Mib})", $"Write(0,{Mib})", "UpdateProperties"]);
        fx.OperationNames.ShouldBe(["DeleteLayout"]);
    }

    [Fact]
    public async Task DeleteLayoutAfterACommitWhileOnlineZerosOnlyOutsideThePartitions()
    {
        await using var fx = new Fixture();
        const long tailGap = 344 * 1024; // The last partition ends 344 KiB before the end of the disk, so the last MiB is not free.
        await fx.GoOfflineAsync();
        await fx.Disk.CommitLayoutAsync(Layout(partitionEnd: DiskLength - tailGap), CancellationToken.None);
        await fx.Disk.BringOnlineAsync(CancellationToken.None);
        fx.Device.Calls.Clear();
        fx.Device.Writes.Clear();

        await fx.Disk.DeleteLayoutAsync(CancellationToken.None);

        fx.Device.Writes.Select(w => (w.Offset, w.Data.Length)).ToArray()
            .ShouldBe(new[] { (DiskLength - tailGap, (int)tailGap), (0L, (int)Mib) });
        fx.Device.Writes.All(w => w.Data.All(b => b == 0)).ShouldBeTrue();
        fx.Device.Calls.ShouldBe([$"Write({DiskLength - tailGap},{tailGap})", $"Write(0,{Mib})", "UpdateProperties"]);
    }

    [Fact]
    public async Task DisposeReleasesTheLocksThenBringsAnOfflineDiskBackOnline()
    {
        var fx = new Fixture();
        await fx.GoOfflineAsync();
        fx.Device.Calls.Clear();

        await fx.Disk.DisposeAsync();

        fx.Device.Calls.ShouldBe([$"Release({VolumeA})", $"Release({VolumeB})", "SetOffline(False)", "UpdateProperties", "DeviceDispose"]);
        fx.OperationNames.ShouldBe(["Lock", "Offline", "Online"]);
    }

    [Fact]
    public async Task DisposeIsIdempotent()
    {
        var fx = new Fixture();
        await fx.GoOfflineAsync();

        await fx.Disk.DisposeAsync();
        await fx.Disk.DisposeAsync();

        fx.Device.DisposeCount.ShouldBe(1);
    }

    [Fact]
    public async Task DisposeOfAnOnlineDiskOnlyDisposesTheDevice()
    {
        var fx = new Fixture();

        await fx.Disk.DisposeAsync();

        fx.Device.Calls.ShouldBe(["DeviceDispose"]);
    }

    [Fact]
    public async Task OperationsAfterDisposeAreRefused()
    {
        var fx = new Fixture();
        await fx.Disk.DisposeAsync();

        await Should.ThrowAsync<ObjectDisposedException>(async () =>
            await fx.Disk.WriteAsync(Mib, new byte[512], CancellationToken.None));
    }

    /// <summary>One partition from the first MiB to <paramref name="partitionEnd"/>. The default end is 65 MiB.</summary>
    private static TargetLayout Layout(int sector = 512, long? partitionEnd = null) => new(
        DiskGuid,
        ByteSize.FromMiB(256),
        new SectorSize(sector),
        [new TargetLayoutEntry(
            new GptPartitionType(new Guid("EBD0A0A2-B9E5-4433-87C0-68B6B72699C7")),
            new PartitionGuid(PartitionGuid),
            new Extent(Mib, (partitionEnd ?? Mib + 64 * Mib) - Mib),
            0UL,
            "Data")]);

    /// <summary>A target disk over the fake device, with its own log and clock.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        public Fixture(long length = DiskLength, int sectorSize = 512, Guid? gptDiskId = null)
        {
            Device = new FakeTargetDiskDevice(length, sectorSize, gptDiskId);
            Log = new RecordingExecutionLog();
            Disk = new TargetDisk(
                Device,
                new DiskOperationRecorder(Log, new ManualClock(DateTimeOffset.UnixEpoch)),
                "disk-under-test");
        }

        public FakeTargetDiskDevice Device { get; }

        public RecordingExecutionLog Log { get; }

        public TargetDisk Disk { get; }

        public IEnumerable<string> OperationNames =>
            Log.Events.Where(e => e.Event == "DiskOperation").Select(e => (string)e.Properties["Operation"]!);

        public Task GoOfflineAsync() => Disk.LockAndTakeOfflineAsync(CancellationToken.None);

        public ValueTask DisposeAsync() => Disk.DisposeAsync();
    }
}
