using WinCopy.Application.Ports;
using WinCopy.Domain.BootRepair;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de IOfflineRegistryEditor que registra las reescrituras de MountedDevices solicitadas.</summary>
public sealed class FakeOfflineRegistryEditor : FakeBase, IOfflineRegistryEditor
{
    /// <summary>Reescrituras solicitadas, en orden, con la raíz de Windows a la que se aplicaron.</summary>
    public List<(string WindowsRoot, MountedDevicesRewrite Rewrite)> Rewrites { get; } = [];

    /// <summary>Registra la reescritura solicitada.</summary>
    public Task RewriteMountedDevicesAsync(string windowsRoot, MountedDevicesRewrite rewrite, CancellationToken ct) =>
        Complete(nameof(RewriteMountedDevicesAsync), ct, () => Rewrites.Add((windowsRoot, rewrite)));
}
