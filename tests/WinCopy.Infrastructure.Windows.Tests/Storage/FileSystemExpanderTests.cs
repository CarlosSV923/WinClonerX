using System.ComponentModel;
using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Logging;
using WinCopy.Infrastructure.Windows.Storage;
using WinCopy.Infrastructure.Windows.Tests.Fakes;
using WinCopy.Infrastructure.Windows.Tests.Logging;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Storage;

// The volume is replaced by a fake. No test opens a volume, so no FSCTL reaches the OS.
public sealed class FileSystemExpanderTests
{
    private const long GiB = 1L << 30;
    private const string VolumeGuidPath = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\";
    private const string VolumeDevicePath = @"\\?\Volume{11111111-2222-3333-4444-555555555555}";

    private static readonly DateTimeOffset Start = new(2026, 10, 13, 10, 0, 0, TimeSpan.Zero);
    private static readonly PartitionGuid Partition = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    // 2 GiB partition with 512-byte sectors: 4194304 sectors, so the target is 4194303.
    private const long PartitionLength = 2 * GiB;
    private const long TargetSectors = 4_194_303;

    [Theory]
    [InlineData(GiB, 512, 2_097_151)]
    [InlineData(GiB, 4096, 262_143)]
    [InlineData(2 * GiB, 512, 4_194_303)]
    public void TargetKeepsTheLastSectorOfThePartitionForTheBackupBootSector(long length, long bytesPerSector, long expected)
    {
        FileSystemExpander.TargetSectorCount(length, bytesPerSector).ShouldBe(expected);
    }

    [Fact]
    public void TargetSectorCountRejectsZeroSectorSize()
    {
        Should.Throw<ArgumentOutOfRangeException>(() => FileSystemExpander.TargetSectorCount(GiB, 0));
    }

    [Fact]
    public async Task ExtendsTheVolumeWhenThePartitionIsLargerThanIt()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000));
        var (expander, access, _) = Build(session);

        await expander.ExpandToPartitionAsync(Partition, CancellationToken.None);

        session.ExtendCalls.ShouldBe([TargetSectors]);
    }

    [Fact]
    public async Task OpensTheVolumeWithoutItsTrailingBackslash()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000));
        var (expander, access, _) = Build(session);

        await expander.ExpandToPartitionAsync(Partition, CancellationToken.None);

        access.OpenedPaths.ShouldBe([VolumeDevicePath]);
    }

    [Fact]
    public async Task DoesNotCallExtendWhenTheVolumeAlreadyFillsThePartition()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, TargetSectors));
        var (expander, _, _) = Build(session);

        await expander.ExpandToPartitionAsync(Partition, CancellationToken.None);

        session.ExtendCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task DoesNotCallExtendWhenTheVolumeIsLargerThanThePartition()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, TargetSectors + 100));
        var (expander, _, _) = Build(session);

        await expander.ExpandToPartitionAsync(Partition, CancellationToken.None);

        session.ExtendCalls.ShouldBeEmpty();
    }

    [Fact]
    public async Task ClosesTheVolumeAfterExpanding()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000));
        var (expander, _, _) = Build(session);

        await expander.ExpandToPartitionAsync(Partition, CancellationToken.None);

        session.Disposed.ShouldBeTrue();
    }

    [Fact]
    public async Task RecordsOneSuccessfulExtendDiskOperation()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000));
        var (expander, _, log) = Build(session);

        await expander.ExpandToPartitionAsync(Partition, CancellationToken.None);

        LogEvent e = log.Events.Single();
        e.Event.ShouldBe("DiskOperation");
        e.Level.ShouldBe(LogLevelKind.Information);
        e.Properties["Operation"].ShouldBe("Extend");
        e.Properties["Result"].ShouldBe("Success");
        e.Properties["Disk"].ShouldBe(Partition.Value.ToString("B"));
    }

    [Fact]
    public async Task FailureWhileExtendingIsMappedToExtendFailedAndRecorded()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000))
        {
            ExtendFailure = new Win32Exception(5),
        };
        var (expander, _, log) = Build(session);

        var ex = await Should.ThrowAsync<PlatformException>(() => expander.ExpandToPartitionAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ExtendFailed);
        ex.InnerException.ShouldBeOfType<Win32Exception>();

        LogEvent e = log.Events.Single();
        e.Level.ShouldBe(LogLevelKind.Error);
        e.Properties["Result"].ShouldBe("Failed");
        e.Properties["Operation"].ShouldBe("Extend");
    }

    [Fact]
    public async Task VolumeOpenFailureIsMappedToExtendFailed()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000));
        var access = new FakeVolumeAccess(session) { OpenFailure = new Win32Exception(5) };
        var expander = new FileSystemExpander(
            new FakeVolumePathResolver(VolumeGuidPath),
            access,
            new DiskOperationRecorder(new RecordingExecutionLog(), new ManualClock(Start)));

        var ex = await Should.ThrowAsync<PlatformException>(() => expander.ExpandToPartitionAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ExtendFailed);
    }

    [Fact]
    public async Task ResolverFailureIsMappedToExtendFailed()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000));
        var resolver = new FakeVolumePathResolver(
            VolumeGuidPath,
            new PlatformException(PlatformErrorCode.DeviceNotFound, "no volume"));
        var (expander, access, _) = Build(session, resolver);

        var ex = await Should.ThrowAsync<PlatformException>(() => expander.ExpandToPartitionAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.ExtendFailed);
        access.OpenedPaths.ShouldBeEmpty();
    }

    [Fact]
    public async Task CancellationBeforeStartThrowsWithoutOpeningAnything()
    {
        var session = new FakeVolumeSession(PartitionLength, new VolumeGeometry(512, 1_000_000));
        var (expander, access, log) = Build(session);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() => expander.ExpandToPartitionAsync(Partition, cts.Token));

        access.OpenedPaths.ShouldBeEmpty();
        log.Events.ShouldBeEmpty();
    }

    private static (FileSystemExpander Expander, FakeVolumeAccess Access, RecordingExecutionLog Log) Build(
        FakeVolumeSession session,
        FakeVolumePathResolver? resolver = null)
    {
        var access = new FakeVolumeAccess(session);
        var log = new RecordingExecutionLog();
        var expander = new FileSystemExpander(
            resolver ?? new FakeVolumePathResolver(VolumeGuidPath),
            access,
            new DiskOperationRecorder(log, new ManualClock(Start)));

        return (expander, access, log);
    }

    private sealed class FakeVolumeAccess(FakeVolumeSession session) : IVolumeAccess
    {
        public List<string> OpenedPaths { get; } = [];

        public Exception? OpenFailure { get; init; }

        public IVolumeSession OpenReadWrite(string volumeDevicePath)
        {
            OpenedPaths.Add(volumeDevicePath);

            if (OpenFailure is not null)
            {
                throw OpenFailure;
            }

            return session;
        }
    }

    private sealed class FakeVolumeSession(long lengthBytes, VolumeGeometry geometry) : IVolumeSession
    {
        public List<long> ExtendCalls { get; } = [];

        public Exception? ExtendFailure { get; init; }

        public bool Disposed { get; private set; }

        public long GetLengthBytes() => lengthBytes;

        public VolumeGeometry GetNtfsGeometry() => geometry;

        public void ExtendToSectors(long sectorCount)
        {
            ExtendCalls.Add(sectorCount);

            if (ExtendFailure is not null)
            {
                throw ExtendFailure;
            }
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }
}
