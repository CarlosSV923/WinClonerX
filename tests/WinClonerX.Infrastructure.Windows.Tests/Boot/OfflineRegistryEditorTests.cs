using System.ComponentModel;
using System.Runtime.InteropServices;
using Shouldly;
using WinClonerX.Application.Errors;
using WinClonerX.Domain.BootRepair;
using WinClonerX.Infrastructure.Windows.Boot;
using WinClonerX.Infrastructure.Windows.Interop;
using Xunit;

namespace WinClonerX.Infrastructure.Windows.Tests.Boot;

// The hive, the privileges and the OS queries are all fakes. No test loads a hive or touches the registry.
public sealed class OfflineRegistryEditorTests
{
    private const string HivePath = @"W:\Windows\System32\config\SYSTEM";
    private const string DriveValue = @"\DosDevices\C:";
    private const string StaleVolumeValue = @"\??\Volume{old}";
    private const string OtherDiskValue = @"\DosDevices\D:";
    private const string MbrValue = @"\??\Volume{mbr}";
    private const string TextValue = @"\DosDevices\E:";
    private const int ErrorAccessDenied = 5;
    private const int ErrorFileNotFound = 2;
    private const int ErrorNotAllAssigned = 1300;

    private static readonly Guid OldWindowsGuid = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid OldDataGuid = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OtherDiskGuid = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid NewWindowsGuid = Guid.Parse("44444444-4444-4444-4444-444444444444");

    [Fact]
    public async Task DeletesStaleEntriesAndSetsTheDriveLetter()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var editor = Build(scenario, hive);

        await editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None);

        ((byte[])hive.Values[DriveValue]).ShouldBe(NewValue());
        hive.Values.ContainsKey(StaleVolumeValue).ShouldBeFalse();
        hive.Deletes.ShouldBe([StaleVolumeValue]);
    }

    [Fact]
    public async Task KeepsEntriesThatAreNotStale()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var editor = Build(scenario, hive);

        await editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None);

        ((byte[])hive.Values[OtherDiskValue]).ShouldBe(MountedDevicesRewrite.GptValue(OtherDiskGuid));
        ((byte[])hive.Values[MbrValue]).Length.ShouldBe(12);
        hive.Values[TextValue].ShouldBe("not binary");
    }

    [Fact]
    public async Task UnloadIsAlwaysCalledWhenTheEditThrows()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        hive.SetFailure = new InvalidOperationException("the value could not be written");
        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        (ex.InnerException is InvalidOperationException).ShouldBeTrue();
        hive.UnloadCalls.ShouldBe(1);
        hive.IsLoaded.ShouldBeFalse();
    }

    [Fact]
    public async Task EveryKeyIsClosedBeforeTheUnload()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        hive.SetFailure = new InvalidOperationException("the value could not be written");
        var editor = Build(scenario, hive);

        await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        hive.OpenKeysAtUnload.ShouldBe([0]);
    }

    [Fact]
    public async Task ThePrivilegesAreRequestedBeforeTheLoad()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var privileges = new FakePrivilegeEnabler();
        var editor = new OfflineRegistryEditor(scenario.Os, scenario.Extents, hive, privileges, TimeSpan.Zero);

        await editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None);

        privileges.Requested.ShouldBe(["SeBackupPrivilege", "SeRestorePrivilege"]);
        hive.LoadCalls.ShouldBe(1);
    }

    [Fact]
    public async Task APrivilegeFailureStopsBeforeTheLoad()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var privileges = new FakePrivilegeEnabler { Failure = new Win32Exception(ErrorNotAllAssigned, "not all assigned") };
        var editor = new OfflineRegistryEditor(scenario.Os, scenario.Extents, hive, privileges, TimeSpan.Zero);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        hive.LoadCalls.ShouldBe(0);
        hive.UnloadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task AFailedLoadIsNotFollowedByAnUnload()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        hive.LoadFailure = new Win32Exception(ErrorFileNotFound, "no hive");
        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        hive.UnloadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task TheRunningWindowsDriveIsRejectedBeforeAnyLoad()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var privileges = new FakePrivilegeEnabler();
        var editor = new OfflineRegistryEditor(scenario.Os, scenario.Extents, hive, privileges, TimeSpan.Zero);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(@"C:\", Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        privileges.Requested.ShouldBeEmpty();
        hive.LoadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task ARootOnASystemDiskIsRejectedBeforeAnyLoad()
    {
        var scenario = new BootScenario();
        scenario.Extents.DisksByVolume[BootScenario.WindowsVolumeDevice] = new HashSet<uint> { 0 };
        var hive = SeededHive();
        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        ex.TechnicalDetail.ShouldContain("holds the running Windows");
        hive.LoadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task AMissingSystemHiveIsRejectedBeforeAnyLoad()
    {
        var scenario = new BootScenario();
        scenario.Os.Files.Clear();
        var hive = SeededHive();
        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        ex.TechnicalDetail.ShouldContain("SYSTEM hive");
        hive.LoadCalls.ShouldBe(0);
    }

    [Theory]
    [InlineData("W:")]
    [InlineData(@"W:\Windows")]
    [InlineData(@"\\server\share\")]
    [InlineData("WW:\\")]
    public async Task AnyRootThatIsNotADriveRootIsRejectedBeforeAnyLoad(string root)
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(root, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        hive.LoadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task AnOsQueryThatFailsRefusesTheEdit()
    {
        var scenario = new BootScenario();
        scenario.Extents.Failure = new InvalidOperationException("the device is not answering");
        var hive = SeededHive();
        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        hive.LoadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task AWrongSizedValueIsRejectedBeforeAnyLoad()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var editor = Build(scenario, hive);
        var badRewrite = new MountedDevicesRewrite('C', new byte[12], []);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, badRewrite, CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        hive.LoadCalls.ShouldBe(0);
    }

    [Fact]
    public async Task TheMountNameIsUniqueAndTheSystemHiveIsLoaded()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        var editor = Build(scenario, hive);

        await editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None);

        (hive.LoadedFrom == HivePath).ShouldBeTrue();
        string mountName = hive.MountName ?? string.Empty;
        mountName.ShouldStartWith("WinClonerXClone_");
        mountName.Length.ShouldBe("WinClonerXClone_".Length + 32);
    }

    [Fact]
    public async Task AccessDeniedOnUnloadIsRetriedUntilItSucceeds()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        hive.UnloadResults.Enqueue(ErrorAccessDenied);
        hive.UnloadResults.Enqueue(ErrorAccessDenied);
        var editor = Build(scenario, hive);

        await editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None);

        hive.UnloadCalls.ShouldBe(3);
        hive.IsLoaded.ShouldBeFalse();
    }

    [Fact]
    public async Task AccessDeniedOnUnloadGivesUpAfterTheRetries()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        for (int i = 0; i <= OfflineRegistryEditor.UnloadRetries; i++)
        {
            hive.UnloadResults.Enqueue(ErrorAccessDenied);
        }

        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        hive.UnloadCalls.ShouldBe(OfflineRegistryEditor.UnloadRetries + 1);
    }

    [Fact]
    public async Task AnUnloadErrorOtherThanAccessDeniedIsNotRetried()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        hive.UnloadResults.Enqueue(ErrorFileNotFound);
        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.Code.ShouldBe(PlatformErrorCode.RegistryEditFailed);
        hive.UnloadCalls.ShouldBe(1);
    }

    [Fact]
    public async Task TheEditErrorIsKeptWhenTheUnloadAlsoFails()
    {
        var scenario = new BootScenario();
        var hive = SeededHive();
        hive.SetFailure = new InvalidOperationException("the value could not be written");
        for (int i = 0; i <= OfflineRegistryEditor.UnloadRetries; i++)
        {
            hive.UnloadResults.Enqueue(ErrorAccessDenied);
        }

        var editor = Build(scenario, hive);

        PlatformException ex = await Should.ThrowAsync<PlatformException>(
            () => editor.RewriteMountedDevicesAsync(BootScenario.WindowsRoot, Rewrite(), CancellationToken.None));

        ex.TechnicalDetail.ShouldContain("the value could not be written");
        ex.TechnicalDetail.ShouldContain("could not be unloaded either");
    }

    [Fact]
    public void TheTokenPrivilegesStructureMatchesWin32()
    {
        // TOKEN_PRIVILEGES with one entry: a count (4 bytes) and one LUID_AND_ATTRIBUTES (12 bytes).
        Marshal.SizeOf<TOKEN_PRIVILEGES>().ShouldBe(16);
        Marshal.SizeOf<LUID>().ShouldBe(8);
    }

    private static OfflineRegistryEditor Build(BootScenario scenario, FakeOfflineHive hive) =>
        new(scenario.Os, scenario.Extents, hive, new FakePrivilegeEnabler(), TimeSpan.Zero);

    // The MountedDevices of a clone that still points at the source disk, with values that must stay.
    private static FakeOfflineHive SeededHive()
    {
        var hive = new FakeOfflineHive();
        hive.Values[DriveValue] = MountedDevicesRewrite.GptValue(OldWindowsGuid);
        hive.Values[StaleVolumeValue] = MountedDevicesRewrite.GptValue(OldDataGuid);
        hive.Values[OtherDiskValue] = MountedDevicesRewrite.GptValue(OtherDiskGuid);
        hive.Values[MbrValue] = new byte[12];
        hive.Values[TextValue] = "not binary";
        return hive;
    }

    private static MountedDevicesRewrite Rewrite() => new(
        'C',
        NewValue(),
        [MountedDevicesRewrite.GptValue(OldWindowsGuid), MountedDevicesRewrite.GptValue(OldDataGuid)]);

    private static byte[] NewValue() => MountedDevicesRewrite.GptValue(NewWindowsGuid);
}
