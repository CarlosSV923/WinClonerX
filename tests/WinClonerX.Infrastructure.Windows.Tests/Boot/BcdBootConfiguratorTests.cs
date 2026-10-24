using System.ComponentModel;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Infrastructure.Windows.Boot;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Boot;

// bcdboot and bcdedit are replaced by recording launchers. No test starts a process.
public sealed class BcdBootConfiguratorTests
{
    private static readonly string[] BcdbootArguments = [@"W:\Windows", "/s", "S:", "/f", "UEFI"];

    [Fact]
    public async Task NoStoreRunsBcdbootWithTheExactArgumentsAndNoBcdedit()
    {
        var s = new BootScenario();
        s.Launcher.Respond = (_, _) => CreateStoreByBcdboot(s);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        var call = s.Launcher.Calls.Single();
        call.Path.ShouldBe(Path.Combine(BootScenario.SystemDirectory, "bcdboot.exe"));
        call.Arguments.ShouldBe(BcdbootArguments);
    }

    [Fact]
    public async Task RunningDriveAsWindowsRootIsRefusedBeforeAnyProcessStarts()
    {
        var s = new BootScenario();
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(@"C:\", BootScenario.EspRoot, null, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task WindowsVolumeOnTheSystemDiskIsRefusedBeforeAnyProcessStarts()
    {
        var s = new BootScenario();
        s.Extents.DisksByVolume[BootScenario.WindowsVolumeDevice] = new HashSet<uint> { 0 };
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, null, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    [Fact]
    public async Task NonZeroBcdbootExitIsBootConfigFailedWithTheToolOutput()
    {
        var s = new BootScenario();
        s.Launcher.Result = new ToolResult(2, "bcdboot out text", "bcdboot err text");
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, null, CancellationToken.None));

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
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, null, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Launcher.Calls.Count.ShouldBe(1);
    }

    [Fact]
    public async Task StartFailureIsBootConfigFailed()
    {
        var s = new BootScenario();
        s.Launcher.Failure = new Win32Exception(2);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, null, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
    }

    [Fact]
    public async Task FullRetargetRunsTheExactBcdeditSequenceAndNoBcdboot()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        BcdeditCalls(s).ShouldBe(
        [
            Call("/enum {bootmgr} /v"),
            Call("/enum {default} /v"),
            Call($"/enum {FakeBcdStore.Resume} /v"),
            Call("/enum {memdiag} /v"),
            Call($"/enum {FakeBcdStore.Recovery} /v"),
            Call("/set {bootmgr} device partition=S:"),
            Call($"/set {FakeBcdStore.Loader} device partition=W:"),
            Call($"/set {FakeBcdStore.Loader} osdevice partition=W:"),
            Call($"/set {FakeBcdStore.Resume} device partition=W:"),
            Call($"/set {FakeBcdStore.Resume} filedevice partition=W:"),
            Call("/set {memdiag} device partition=S:"),
            Call($@"/set {FakeBcdStore.Recovery} device ramdisk=[R:]\Recovery\WindowsRE\Winre.wim,{FakeBcdStore.RamdiskOptions}"),
            Call($@"/set {FakeBcdStore.Recovery} osdevice ramdisk=[R:]\Recovery\WindowsRE\Winre.wim,{FakeBcdStore.RamdiskOptions}"),
            Call($"/set {FakeBcdStore.RamdiskOptions} ramdisksdidevice partition=R:"),
            Call("/enum {default} /v"),
        ]);
        s.Launcher.Calls.Count.ShouldBe(BcdeditCalls(s).Length, "bcdboot is not run after a retarget");
    }

    [Fact]
    public async Task RetargetKeepsEveryEntryAndItsSettings()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        int entriesBefore = store.Entries.Count;
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        store.Entries.Count.ShouldBe(entriesBefore, "no entry is added or removed");
        Dictionary<string, string> loader = store.Entries[FakeBcdStore.Loader].Elements;
        loader["device"].ShouldBe("partition=W:");
        loader["osdevice"].ShouldBe("partition=W:");
        loader["hypervisorlaunchtype"].ShouldBe("Auto");
        loader["locale"].ShouldBe("es-MX");
        loader["recoverysequence"].ShouldBe(FakeBcdStore.Recovery);
        store.Entries[FakeBcdStore.BootManager].Elements["device"].ShouldBe("partition=S:");
    }

    [Fact]
    public async Task StoreWithoutResumeObjectOrMemDiagOnlyWritesWhatExists()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        store.Entries.Remove(FakeBcdStore.MemDiag);
        store.Entries[FakeBcdStore.Loader].Elements.Remove("resumeobject");
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        BcdeditCalls(s).ShouldBe(
        [
            Call("/enum {bootmgr} /v"),
            Call("/enum {default} /v"),
            Call("/enum {memdiag} /v"),
            Call($"/enum {FakeBcdStore.Recovery} /v"),
            Call("/set {bootmgr} device partition=S:"),
            Call($"/set {FakeBcdStore.Loader} device partition=W:"),
            Call($"/set {FakeBcdStore.Loader} osdevice partition=W:"),
            Call($@"/set {FakeBcdStore.Recovery} device ramdisk=[R:]\Recovery\WindowsRE\Winre.wim,{FakeBcdStore.RamdiskOptions}"),
            Call($@"/set {FakeBcdStore.Recovery} osdevice ramdisk=[R:]\Recovery\WindowsRE\Winre.wim,{FakeBcdStore.RamdiskOptions}"),
            Call($"/set {FakeBcdStore.RamdiskOptions} ramdisksdidevice partition=R:"),
            Call("/enum {default} /v"),
        ]);
    }

    [Fact]
    public async Task StoreWithoutRecoverySequenceSkipsWinre()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        store.Entries[FakeBcdStore.Loader].Elements.Remove("recoverysequence");
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        BcdeditCalls(s).Any(call => call.Contains(FakeBcdStore.Recovery, StringComparison.Ordinal)).ShouldBeFalse();
    }

    [Fact]
    public async Task WithoutARecoveryRootWinreIsNotRetargeted()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, null, CancellationToken.None);

        BcdeditCalls(s).Any(call => call.Contains(FakeBcdStore.Recovery, StringComparison.Ordinal)).ShouldBeFalse();
        BcdeditCalls(s).Any(call => call.Contains(FakeBcdStore.RamdiskOptions, StringComparison.Ordinal)).ShouldBeFalse();
        store.Entries[FakeBcdStore.Loader].Elements["recoverysequence"].ShouldBe(FakeBcdStore.Recovery);
    }

    [Fact]
    public async Task StoreWithoutDefaultLoaderFallsBackToBcdboot()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        store.Entries[FakeBcdStore.BootManager].Elements.Remove("default");
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        await configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None);

        BcdeditCalls(s).ShouldBe([Call("/enum {bootmgr} /v")]);
        s.Launcher.Calls.Single(call => call.Path.EndsWith("bcdboot.exe", StringComparison.Ordinal))
            .Arguments.ShouldBe(BcdbootArguments);
    }

    [Fact]
    public async Task DefaultLoaderThatDoesNotShowTheCloneIsAVerificationFailure()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        store.IgnoredWrites.Add($"{FakeBcdStore.Loader} osdevice");
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        ex.TechnicalDetail.ShouldContain("does not point to 'W:'");
        ex.TechnicalDetail.ShouldContain("osdevice");
        s.Launcher.Calls.ShouldAllBe(call => call.Path.EndsWith("bcdedit.exe", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AFailedWriteStopsTheRetargetAndIsBootConfigFailed()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore { WriteExitCode = 5 };
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        ex.TechnicalDetail.ShouldContain("Acceso denegado");
        BcdeditCalls(s).Count(call => call.Contains(" /set ", StringComparison.Ordinal)).ShouldBe(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Error: the boot configuration data store could not be opened.")]
    public async Task MalformedBcdeditOutputFailsClosedBeforeAnyWrite(string output)
    {
        var s = new BootScenario();
        s.Os.Files.Add(BootScenario.BcdFile);
        s.Launcher.Respond = (_, _) => new ToolResult(0, output, string.Empty);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        BcdeditCalls(s).ShouldAllBe(call => call.Contains(" /enum ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DefaultLoaderThatIsNotAGuidFailsClosedBeforeAnyWrite()
    {
        var s = new BootScenario();
        var store = new FakeBcdStore();
        store.Entries[FakeBcdStore.BootManager].Elements["default"] = "{current}";
        UseStore(s, store);
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, BootScenario.RecoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        BcdeditCalls(s).Count(call => call.Contains(" /set ", StringComparison.Ordinal)).ShouldBe(0);
    }

    [Theory]
    [InlineData("R")]
    [InlineData(@"S:\")]
    public async Task RecoveryRootThatIsMalformedOrOnTheEspIsRefusedBeforeAnyProcessStarts(string recoveryRoot)
    {
        var s = new BootScenario();
        UseStore(s, new FakeBcdStore());
        var configurator = new BcdBootConfigurator(s.Runner, s.Safety, s.Os);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => configurator.RebuildAsync(BootScenario.WindowsRoot, BootScenario.EspRoot, recoveryRoot, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.BootConfigFailed);
        s.Launcher.Calls.ShouldBeEmpty();
    }

    // Every bcdedit call must name the clone store first. The fake store throws when that is not so.
    private static string Call(string tail) => $"/store {BootScenario.BcdFile} {tail}";

    private static string[] BcdeditCalls(BootScenario s) =>
        s.Launcher.Calls
            .Where(call => call.Path.EndsWith("bcdedit.exe", StringComparison.Ordinal))
            .Select(call => string.Join(' ', call.Arguments))
            .ToArray();

    // The clone store exists, bcdedit answers from the fake store, and a bcdboot run would create the store as usual.
    private static void UseStore(BootScenario s, FakeBcdStore store)
    {
        s.Os.Files.Add(BootScenario.BcdFile);
        s.Launcher.Respond = (path, arguments) => path.EndsWith("bcdedit.exe", StringComparison.Ordinal)
            ? store.Run(arguments)
            : CreateStoreByBcdboot(s);
    }

    private static ToolResult CreateStoreByBcdboot(BootScenario s)
    {
        s.Os.Files.Add(BootScenario.BcdFile);

        return new ToolResult(0, string.Empty, string.Empty);
    }
}
