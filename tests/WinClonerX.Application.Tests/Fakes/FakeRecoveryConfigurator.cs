using WinClonerX.Application.Ports;
using WinClonerX.Application.Ports.Models;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de IRecoveryConfigurator con resultado configurable y registro de configuraciones.</summary>
public sealed class FakeRecoveryConfigurator : FakeBase, IRecoveryConfigurator
{
    /// <summary>Resultado que devuelve ConfigureAsync.</summary>
    public RecoveryConfigResult Result { get; set; } = new(true, null);

    /// <summary>Configuraciones solicitadas, en orden.</summary>
    public List<(string WindowsRoot, string RecoveryRoot)> Configurations { get; } = [];

    /// <summary>Registra la configuración solicitada y devuelve el resultado configurado.</summary>
    public Task<RecoveryConfigResult> ConfigureAsync(string windowsRoot, string recoveryRoot, CancellationToken ct) =>
        Reply(nameof(ConfigureAsync), () =>
        {
            Configurations.Add((windowsRoot, recoveryRoot));
            return Result;
        }, ct);
}
