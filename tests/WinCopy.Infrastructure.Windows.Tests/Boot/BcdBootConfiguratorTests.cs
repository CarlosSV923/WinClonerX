using System.ComponentModel;
using Shouldly;
using WinCopy.Application.Errors;
using WinCopy.Infrastructure.Windows.Boot;
using Xunit;

namespace WinCopy.Infrastructure.Windows.Tests.Boot;

// bcdboot is replaced by a recording launcher. No test starts a process.
public sealed class BcdBootConfiguratorTests
{
    private static readonly string[] ExpectedArguments = [@"W:\Windows", "/s", "S:", "/f", "UEFI"];

    [Fact]
    public async Task RunsBcdbootWithTheExactArgumentsAndSlashS()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.BcdFile);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, CancellationToken.None);

        var call = s.Launcher.Calls.Single();
        call.Path.ShouldBe(Path.Combine(BootScenario.SystemDirectory, "bcdboot.exe"));
        call.Arguments.ShouldBe(ExpectedArguments);
    }

    [Fact]
    public async Task RunningDriveAsWindowsRootIsRefusedBeforeAnyProcessStarts()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.BcdFile);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(@"C:\", BootScenario.EspRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WindowsVolumeOnTheSystemDiskIsRefusedBeforeAnyProcessStarts()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.BcdFile);
        s.Extents.DisksByVolume[BootScenario.WindowsVolumeDevice] = new HashSet<uint> { 0 };
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonZeroExitIsBootConfigFailedWithTheToolOutput()
    {
        var s = new BootScenario();
        s.Launcher.Result = new ToolResult(2, "bcdboot out text", "bcdboot err text");
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        ex.TechnicalDetail.ShouldContain("bcdboot out text");
        ex.TechnicalDetail.ShouldContain("bcdboot err text");
    }

    [Fact]
    public async Task SuccessWithoutAClonedBcdStoreIsBootConfigFailed()
    {
        var s = new BootScenario();
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Launcher.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task SuccessWithAClonedBcdStoreCompletes()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.BcdFile);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, CancellationToken.None);

        s.Launcher.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StartFailureIsBootConfigFailed()
    {
        var s = new BootScenario();
        s.Launcher.Failure = new Win32Exception(2);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }
}
