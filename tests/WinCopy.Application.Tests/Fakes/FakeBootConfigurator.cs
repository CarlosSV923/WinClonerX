using WinCopy.Application.Ports;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de IBootConfigurator que registra las reconstrucciones de BCD.</summary>
public sealed class FakeBootConfigurator : FakeBase, IBootConfigurator
{
    /// <summary>Reconstrucciones solicitadas, en orden.</summary>
    public List<(string WindowsRoot, string EspRoot)> Rebuilds { get; } = [];

    /// <summary>Registra la reconstrucción solicitada.</summary>
    public Task RebuildAsync(string windowsRoot, string espRoot, CancellationToken ct) =>
        Complete(nameof(RebuildAsync), ct, () => Rebuilds.Add((windowsRoot, espRoot)));
}
