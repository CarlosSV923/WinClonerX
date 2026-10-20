using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;
using WinCopy.Domain.BootRepair;

namespace WinCopy.Infrastructure.IntegrationTests.Cloning;

// The reference system disk is not a real Windows installation, so the boot repair has nothing real to act on: no
// SYSTEM hive to load, no BCD to rebuild, no WinRE image to point. These fakes record the calls the orchestrator makes
// with the mounted roots. The real boot repair (offline registry, bcdboot, reagentc) is validated in the VM checkpoint S2.

/// <summary>Records <see cref="RewriteMountedDevicesAsync"/> calls. Does not touch any registry.</summary>
internal sealed class RecordingOfflineRegistryEditor : IOfflineRegistryEditor
{
    private readonly List<(string WindowsRoot, MountedDevicesRewrite Rewrite)> _calls = [];

    public IReadOnlyList<(string WindowsRoot, MountedDevicesRewrite Rewrite)> Calls => _calls;

    public Task RewriteMountedDevicesAsync(string windowsRoot, MountedDevicesRewrite rewrite, CancellationToken ct)
    {
        _calls.Add((windowsRoot, rewrite));
        return Task.CompletedTask;
    }
}

/// <summary>Records <see cref="RebuildAsync"/> calls. Runs no bcdboot.</summary>
internal sealed class RecordingBootConfigurator : IBootConfigurator
{
    private readonly List<(string WindowsRoot, string EspRoot, string? RecoveryRoot)> _calls = [];

    public IReadOnlyList<(string WindowsRoot, string EspRoot, string? RecoveryRoot)> Calls => _calls;

    public Task RebuildAsync(string windowsRoot, string espRoot, string? recoveryRoot, CancellationToken ct)
    {
        _calls.Add((windowsRoot, espRoot, recoveryRoot));
        return Task.CompletedTask;
    }
}

/// <summary>Records <see cref="ConfigureAsync"/> calls and reports WinRE as configured. Runs no reagentc.</summary>
internal sealed class RecordingRecoveryConfigurator : IRecoveryConfigurator
{
    private readonly List<(string WindowsRoot, string RecoveryRoot)> _calls = [];

    public IReadOnlyList<(string WindowsRoot, string RecoveryRoot)> Calls => _calls;

    public Task<RecoveryConfigResult> ConfigureAsync(string windowsRoot, string recoveryRoot, CancellationToken ct)
    {
        _calls.Add((windowsRoot, recoveryRoot));
        return Task.FromResult(new RecoveryConfigResult(Configured: true, Detail: null));
    }
}
