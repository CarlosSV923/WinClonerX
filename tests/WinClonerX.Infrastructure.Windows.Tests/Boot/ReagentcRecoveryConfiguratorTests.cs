using System.ComponentModel;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Application.Ports.Models;
using WinClonerX.Infrastructure.Windows.Boot;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Boot;

// reagentc is replaced by a recording launcher. No test starts a process.
public sealed class ReagentcRecoveryConfiguratorTests
{
    private static readonly string[] ExpectedArguments =
        ["/setreimage", "/path", @"R:\Recovery\WindowsRE", "/target", @"W:\Windows"];

    [Fact]
    public async Task RunsReagentcWithTheExactArgumentsAndReportsSuccess()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.WinreImage);
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        RecoveryConfigResult result = await configurator.ConfigureAsync(
            BootScenario.WindowsRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        result.Configured.ShouldBeTrue();
        result.Detail.ShouldBeNull();
        var call = s.Launcher.Calls.Single();
        call.Path.ShouldBe(Path.Combine(BootScenario.SystemDirectory, "reagentc.exe"));
        call.Arguments.ShouldBe(ExpectedArguments);
    }

    [Fact]
    public async Task MissingWinreImageIsAWarningAndNoProcessRuns()
    {
        var s = new BootScenario();
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        RecoveryConfigResult result = await configurator.ConfigureAsync(
            BootScenario.WindowsRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        result.Configured.ShouldBeFalse();
        result.Detail.ShouldBe("WinRE image not found on the recovery partition");
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonZeroExitIsAWarningWithTheToolOutput()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.WinreImage);
        s.Launcher.Result = new ToolResult(1, "reagentc out text", "reagentc err text");
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        RecoveryConfigResult result = await configurator.ConfigureAsync(
            BootScenario.WindowsRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        result.Configured.ShouldBeFalse();
        string detail = result.Detail ?? string.Empty;
        detail.ShouldContain("reagentc out text");
        detail.ShouldContain("reagentc err text");
    }

    [Fact]
    public async Task StartFailureIsAWarningNotAThrow()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.WinreImage);
        s.Launcher.Failure = new Win32Exception(2);
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        RecoveryConfigResult result = await configurator.ConfigureAsync(
            BootScenario.WindowsRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        result.Configured.ShouldBeFalse();
        result.Detail.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task RunningDriveAsWindowsRootThrowsRecoveryConfigFailedBeforeAnyProcess()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.WinreImage);
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.ConfigureAsync(@"C:\", BootScenario.RecoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RecoveryConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task SameLetterForBothRootsThrowsRecoveryConfigFailed()
    {
        var s = new BootScenario();
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.ConfigureAsync(@"W:\", @"W:\", CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RecoveryConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("R")]
    [InlineData("R:")]
    [InlineData(@"R:\Recovery")]
    public async Task MalformedRecoveryRootThrowsRecoveryConfigFailed(string recoveryRoot)
    {
        var s = new BootScenario();
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.ConfigureAsync(BootScenario.WindowsRoot, recoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RecoveryConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task RecoveryVolumeOnTheSystemDiskThrowsRecoveryConfigFailed()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.WinreImage);
        s.Extents.DisksByVolume[BootScenario.RecoveryVolumeDevice] = new HashSet<uint> { 0 };
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.ConfigureAsync(BootScenario.WindowsRoot, BootScenario.RecoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RecoveryConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task MissingSystemHiveThrowsRecoveryConfigFailed()
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.WinreImage);
        s.Os.Files.Remove(BootScenario.WindowsRoot + @"Windows\System32\config\SYSTEM");
        var configurator = new ReagentcRecoveryConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.ConfigureAsync(BootScenario.WindowsRoot, BootScenario.RecoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RecoveryConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }
}
