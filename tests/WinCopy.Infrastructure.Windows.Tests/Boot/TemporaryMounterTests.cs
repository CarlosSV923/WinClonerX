using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Boot;
using WinCopy.Infrastructure.Windows.Tests.Fakes;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Boot;

// Mount points and drive letters are fakes. No test calls SetVolumeMountPoint or reads the real drive list.
public sealed class TemporaryMounterTests
{
    private const string VolumeGuidPath = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\";
    private const int ErrorDirNotEmpty = 145;

    private static readonly PartitionGuid Partition = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    [Fact]
    public async Task MountsAtZWhenNoLetterIsInUse()
    {
        var api = new FakeMountPointApi();
        var mounter = Build(api, drives: []);

        TemporaryMount mount = await mounter.MountAsync(Partition, CancellationToken.None);

        mount.Root.ShouldBe(@"Z:\");
        api.SetAttempts.ShouldBe([@"Z:\"]);
    }

    [Fact]
    public async Task SkipsLettersReportedAsInUse()
    {
        var api = new FakeMountPointApi();
        var mounter = Build(api, drives: [@"C:\", @"Z:\", @"Y:\"]);

        TemporaryMount mount = await mounter.MountAsync(Partition, CancellationToken.None);

        mount.Root.ShouldBe(@"X:\");
        api.SetAttempts.ShouldBe([@"X:\"]);
    }

    [Fact]
    public async Task RetriesTheNextLetterWhenTheFirstFails()
    {
        var api = new FakeMountPointApi { FailingRoots = { @"Z:\" }, FailureCode = ErrorDirNotEmpty };
        var mounter = Build(api, drives: []);

        TemporaryMount mount = await mounter.MountAsync(Partition, CancellationToken.None);

        mount.Root.ShouldBe(@"Y:\");
        api.SetAttempts.ShouldBe([@"Z:\", @"Y:\"]);
    }

    [Fact]
    public async Task OffersOnlyLettersFromZDownToD()
    {
        var api = new FakeMountPointApi();
        var inUse = new List<string>();
        for (char letter = 'E'; letter <= 'Z'; letter++)
        {
            inUse.Add($"{letter}:\\");
        }

        var mounter = Build(api, drives: inUse);

        TemporaryMount mount = await mounter.MountAsync(Partition, CancellationToken.None);

        mount.Root.ShouldBe(@"D:\");
        api.SetAttempts.ShouldBe([@"D:\"]);
    }

    [Fact]
    public async Task NeverOffersTheLettersAToC()
    {
        var api = new FakeMountPointApi();
        var inUse = new List<string>();
        for (char letter = 'D'; letter <= 'Z'; letter++)
        {
            inUse.Add($"{letter}:\\");
        }

        var mounter = Build(api, drives: inUse);

        var ex = await Should.ThrowAsync<PlatformException>(() => mounter.MountAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.MountFailed);
        api.SetAttempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThrowsMountFailedAfterTryingEveryAvailableLetter()
    {
        var failing = new HashSet<string>();
        for (char letter = 'D'; letter <= 'Z'; letter++)
        {
            failing.Add($"{letter}:\\");
        }

        var api = new FakeMountPointApi { FailingRoots = failing, FailureCode = ErrorDirNotEmpty };
        var mounter = Build(api, drives: []);

        var ex = await Should.ThrowAsync<PlatformException>(() => mounter.MountAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.MountFailed);
        api.SetAttempts.Count.ShouldBe(23);
    }

    [Fact]
    public async Task PassesTheVolumeGuidPathOfThePartitionToTheMountCall()
    {
        var api = new FakeMountPointApi();
        var resolver = new FakeVolumePathResolver(VolumeGuidPath);
        var mounter = new TemporaryMounter(resolver, new FakeDrives([]), api);

        await mounter.MountAsync(Partition, CancellationToken.None);

        resolver.LastPartition.ShouldBe(Partition);
        api.VolumePaths.ShouldBe([VolumeGuidPath]);
    }

    [Fact]
    public async Task ResolverFailureIsReturnedWithoutMounting()
    {
        var api = new FakeMountPointApi();
        var resolver = new FakeVolumePathResolver(
            VolumeGuidPath,
            new PlatformException(PlatformErrorCode.DeviceNotFound, "no volume"));
        var mounter = new TemporaryMounter(resolver, new FakeDrives([]), api);

        var ex = await Should.ThrowAsync<PlatformException>(() => mounter.MountAsync(Partition, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.DeviceNotFound);
        api.SetAttempts.ShouldBeEmpty();
    }

    [Fact]
    public async Task ReleaseRemovesTheMountPointOnlyOnce()
    {
        var api = new FakeMountPointApi();
        var mounter = Build(api, drives: []);
        TemporaryMount mount = await mounter.MountAsync(Partition, CancellationToken.None);

        await mount.Release.DisposeAsync();
        await mount.Release.DisposeAsync();

        api.DeleteCalls.ShouldBe([@"Z:\"]);
    }

    [Fact]
    public async Task FailedReleaseIsReportedAndNeverThrows()
    {
        var api = new FakeMountPointApi { DeleteFails = true };
        var reports = new List<string>();
        var mounter = Build(api, drives: [], onReleaseFailure: reports.Add);
        TemporaryMount mount = await mounter.MountAsync(Partition, CancellationToken.None);

        await mount.Release.DisposeAsync();
        await mount.Release.DisposeAsync();

        reports.Count.ShouldBe(1);
        reports[0].ShouldContain(@"Z:\");
        api.DeleteCalls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task ThrowingReportCallbackDoesNotEscapeFromRelease()
    {
        var api = new FakeMountPointApi { DeleteFails = true };
        var mounter = Build(api, drives: [], onReleaseFailure: _ => throw new InvalidOperationException("log is closed"));
        TemporaryMount mount = await mounter.MountAsync(Partition, CancellationToken.None);

        await mount.Release.DisposeAsync();

        api.DeleteCalls.Count.ShouldBe(1);
    }

    private static TemporaryMounter Build(FakeMountPointApi api, IReadOnlyList<string> drives, Action<string>? onReleaseFailure = null)
    {
        return new TemporaryMounter(
            new FakeVolumePathResolver(VolumeGuidPath),
            new FakeDrives(drives),
            api,
            onReleaseFailure);
    }

    private sealed class FakeDrives(IReadOnlyList<string> roots) : ILogicalDriveSource
    {
        public IReadOnlyList<string> GetLogicalDrives() => roots;
    }

    private sealed class FakeMountPointApi : IMountPointApi
    {
        public List<string> SetAttempts { get; } = [];

        public List<string> VolumePaths { get; } = [];

        public HashSet<string> FailingRoots { get; init; } = [];

        public int FailureCode { get; init; }

        public List<string> DeleteCalls { get; } = [];

        public bool DeleteFails { get; init; }

        public bool TrySetMountPoint(string mountRoot, string volumeGuidPath, out int win32Error)
        {
            SetAttempts.Add(mountRoot);
            VolumePaths.Add(volumeGuidPath);

            if (FailingRoots.Contains(mountRoot))
            {
                win32Error = FailureCode;
                return false;
            }

            win32Error = 0;
            return true;
        }

        public bool TryDeleteMountPoint(string mountRoot, out int win32Error)
        {
            DeleteCalls.Add(mountRoot);
            win32Error = DeleteFails ? 5 : 0;
            return !DeleteFails;
        }
    }
}
