using Shouldly;
using WinClonerX.Infrastructure.Windows.Storage;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Storage;

public sealed class VolumePathTests
{
    private const string Guid1 = "{1c5367fb-d302-425a-9f83-460d2490de24}";

    [Theory]
    [InlineData(@"\\?\Volume{1c5367fb-d302-425a-9f83-460d2490de24}\")]
    [InlineData(@"\\?\Volume{1C5367FB-D302-425A-9F83-460D2490DE24}\")]
    public void AcceptsTheVolumeGuidPathShape(string path) => VolumePath.IsVolumeGuidPath(path).ShouldBeTrue();

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(@"C:\")]
    [InlineData(@"\\?\Volume{1c5367fb-d302-425a-9f83-460d2490de24}")]
    [InlineData(@"\\?\GLOBALROOT\Device\HarddiskVolume1\")]
    [InlineData(@"\\?\Volume{1c5367fb-d302-425a-9f83-460d2490de24}\' OR 1=1 --")]
    [InlineData(@"\\?\Volume{not-a-guid}\")]
    public void RejectsAnythingElse(string? path) => VolumePath.IsVolumeGuidPath(path).ShouldBeFalse();

    [Fact]
    public void NormalizeDropsTheTrailingBackslashAndUpperCases()
    {
        VolumePath.Normalize(@"\\?\Volume" + Guid1 + @"\").ShouldBe(@"\\?\VOLUME{1C5367FB-D302-425A-9F83-460D2490DE24}");
    }

    [Fact]
    public void SameVolumeIgnoresTrailingBackslashAndCase()
    {
        VolumePath.IsSameVolume(@"\\?\Volume" + Guid1 + @"\", @"\\?\VOLUME" + Guid1.ToUpperInvariant().TrimEnd('}') + "}").ShouldBeTrue();
    }

    [Fact]
    public void SameVolumeIsFalseWhenEitherSideIsNull()
    {
        VolumePath.IsSameVolume(null, @"\\?\Volume" + Guid1 + @"\").ShouldBeFalse();
        VolumePath.IsSameVolume(@"\\?\Volume" + Guid1 + @"\", null).ShouldBeFalse();
    }

    [Fact]
    public void DifferentVolumesAreNotTheSame()
    {
        VolumePath.IsSameVolume(@"\\?\Volume" + Guid1 + @"\", @"\\?\Volume{00000000-0000-0000-0000-000000000000}\").ShouldBeFalse();
    }

    [Fact]
    public void DevicePathHasNoTrailingBackslash()
    {
        VolumePath.ToDevicePath(@"\\?\Volume" + Guid1 + @"\").ShouldBe(@"\\?\Volume" + Guid1);
    }
}
