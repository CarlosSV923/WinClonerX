using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Ports;

/// <summary>Configura el entorno de recuperación (WinRE) del clon (R7).</summary>
public interface IRecoveryConfigurator
{
    /// <summary>Ejecuta reagentc para apuntar WinRE a la partición de recuperación del clon; no configurarlo es un aviso, no un fallo.</summary>
    Task<RecoveryConfigResult> ConfigureAsync(string windowsRoot, string recoveryRoot, CancellationToken ct);
}
