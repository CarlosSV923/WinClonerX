using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Domain.Shared;
using WinCopy.Infrastructure.Windows.Storage;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Storage;

// The WMI query is replaced by a fake. No test queries the Storage Management API.
public sealed class StorageVolumePathResolverTests
{
    private const string VolumeA = @"\\?\Volume{11111111-2222-3333-4444-555555555555}\";
    private const string VolumeB = @"\\?\Volume{66666666-7777-8888-9999-000000000000}\";

    private static readonly PartitionGuid Partition = new(Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee"));

    [Fact]
    public void ReturnsTheFirstVolumeGuidPathAmongOtherAccessPaths()
    {
        var resolver = new StorageVolumePathResolver(new FakeStorageQuery([@"C:\", VolumeA, VolumeB]));

        resolver.GetVolumeGuidPath(Partition).ShouldBe(VolumeA);
    }

    [Fact]
    public void QueriesWithTheGptPartitionGuid()
    {
        var query = new FakeStorageQuery([VolumeA]);

        new StorageVolumePathResolver(query).GetVolumeGuidPath(Partition);

        query.LastGuid.ShouldBe(Partition.Value);
    }

    [Fact]
    public void MatchesTheVolumePrefixIgnoringCase()
    {
        var resolver = new StorageVolumePathResolver(new FakeStorageQuery([@"\\?\volume{11111111-2222-3333-4444-555555555555}\"]));

        resolver.GetVolumeGuidPath(Partition).ShouldBe(@"\\?\volume{11111111-2222-3333-4444-555555555555}\");
    }

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"D:\Mounts\Data\")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume3\")]
    [InlineData(@"\\?\Volume{11111111-2222-3333-4444-555555555555}")]
    [InlineData(@"\\?\Volume{11111111-2222-3333-4444-555555555555}\extra")]
    public void ThrowsDeviceNotFoundWhenNoAccessPathIsAVolumeGuidPath(string accessPath)
    {
        var resolver = new StorageVolumePathResolver(new FakeStorageQuery([accessPath]));

        var ex = Should.Throw<PlatformException>(() => resolver.GetVolumeGuidPath(Partition));

        ex.Code.ShouldBe(PlatformErrorCode.DeviceNotFound);
    }

    [Fact]
    public void ThrowsDeviceNotFoundWhenThePartitionHasNoAccessPaths()
    {
        var resolver = new StorageVolumePathResolver(new FakeStorageQuery([]));

        var ex = Should.Throw<PlatformException>(() => resolver.GetVolumeGuidPath(Partition));

        ex.Code.ShouldBe(PlatformErrorCode.DeviceNotFound);
    }

    private sealed class FakeStorageQuery(IReadOnlyList<string> accessPaths) : IStorageQuery
    {
        public Guid? LastGuid { get; private set; }

        public IReadOnlyList<string> GetAccessPaths(Guid partitionGuid)
        {
            LastGuid = partitionGuid;
            return accessPaths;
        }
    }
}
