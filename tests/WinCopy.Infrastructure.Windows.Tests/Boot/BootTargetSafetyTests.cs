using Shouldly;
using WinCopy.Application.Errors;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Boot;

// The OS queries are fakes. No test reads a real volume, the registry or the running system.
public sealed class BootTargetSafetyTests
{
    [Fact]
    public void AcceptsCloneRootsOnADiskThatHoldsNoRunningSystem()
    {
        var s = new BootScenario();

        Should.NotThrow(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot));
    }

    [Fact]
    public void RefusesTheRunningWindowsDriveAsTheWindowsRootBeforeAnyVolumeIsQueried()
    {
        var s = new BootScenario();

        PlatformException ex = Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(@"C:\", BootScenario.EspRoot));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Os.VolumeLookups.ShouldBeEmpty();
    }

    [Fact]
    public void RefusesTheRunningWindowsDriveAsTheEspRoot()
    {
        var s = new BootScenario();

        PlatformException ex = Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, @"C:\"));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesAWindowsVolumeOnTheRunningSystemDisk()
    {
        var s = new BootScenario();
        s.Extents.DisksByVolume[BootScenario.WindowsVolumeDevice] = new HashSet<uint> { 0 };

        PlatformException ex = Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesAnEspVolumeOnTheRunningSystemDisk()
    {
        var s = new BootScenario();
        s.Extents.DisksByVolume[BootScenario.EspVolumeDevice] = new HashSet<uint> { 0 };

        PlatformException ex = Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesAVolumeThatSpansADiskWithTheRunningBootPartition()
    {
        var s = new BootScenario();
        s.Extents.DisksByVolume[BootScenario.RunningBootPartitionDevice] = new HashSet<uint> { 2 };
        s.Extents.DisksByVolume[BootScenario.WindowsVolumeDevice] = new HashSet<uint> { 1, 2 };

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesAVolumeWithNoKnownDisks()
    {
        var s = new BootScenario();
        s.Extents.DisksByVolume[BootScenario.EspVolumeDevice] = new HashSet<uint>();

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesWhenTheRunningSystemDisksCannotBeRead()
    {
        var s = new BootScenario();
        s.Extents.Failure = new InvalidOperationException("extents unavailable");

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesWhenTheVolumeOfARootCannotBeResolved()
    {
        var s = new BootScenario();
        s.Os.VolumeGuids.Remove(BootScenario.WindowsRoot);

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesAVolumeGuidPathWithAnUnexpectedShape()
    {
        var s = new BootScenario();
        s.Os.VolumeGuids[BootScenario.WindowsRoot] = @"C:\";

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesAMissingWindowsSystemHive()
    {
        var s = new BootScenario();
        s.Os.Files.Clear();

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, BootScenario.EspRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void RefusesTheSameDriveLetterForBothRootsRegardlessOfCase()
    {
        var s = new BootScenario();

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(@"W:\", @"w:\"))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Theory]
    [InlineData("W")]
    [InlineData("W:")]
    [InlineData("W:/")]
    [InlineData(@"W:\Windows")]
    [InlineData(@"1:\")]
    [InlineData(@"WW:\")]
    [InlineData(@"\\?\Volume{11111111-1111-1111-1111-111111111111}\")]
    [InlineData("")]
    public void RefusesMalformedRootsInEitherPosition(string malformedRoot)
    {
        var s = new BootScenario();

        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(malformedRoot, BootScenario.EspRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        Should.Throw<PlatformException>(() => s.Safety.AssertCloneRoots(BootScenario.WindowsRoot, malformedRoot))
            .Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public void UsesTheFailureCodeTheCallerAsksFor()
    {
        var s = new BootScenario();

        PlatformException ex = Should.Throw<PlatformException>(
            () => s.Safety.AssertCloneRoots(@"C:\", BootScenario.EspRoot, PlatformErrorCode.RecoveryConfigFailed));

        ex.Code.ShouldBe(PlatformErrorCode.RecoveryConfigFailed);
    }
}
