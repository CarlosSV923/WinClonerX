using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Ports;

/// <summary>Observa la llegada y retirada de discos mientras una ejecución está activa (FR-011).</summary>
public interface IDeviceChangeMonitor
{
    /// <summary>Flujo de cambios de dispositivo; cada suscriptor recibe los cambios publicados después de suscribirse.</summary>
    IObservable<DeviceChange> Changes { get; }
}
