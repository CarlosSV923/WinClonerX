using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Ports;

/// <summary>
/// Vigila la retirada de los discos de una ejecución mientras está activa (FR-011, US3 escenario 2). Permite detener el
/// clonado en cuanto el disco de origen o el de destino se desconecta, en lugar de esperar a un error de E/S.
/// </summary>
public interface IDeviceRemovalMonitor
{
    /// <summary>
    /// Empieza a vigilar los discos indicados por su número del sistema. Cuando uno de ellos desaparece, <paramref name="onRemoved"/>
    /// recibe un <see cref="DeviceChange"/> de tipo <see cref="DeviceChangeKind.Removed"/> con ese número, una sola vez por disco.
    /// Puede llamarse desde otro hilo. Disponer la devolución deja de vigilar.
    /// </summary>
    /// <exception cref="WinCopy.Application.Errors.PlatformException">Si no se puede empezar a vigilar; no queda nada activo.</exception>
    IDisposable Watch(IReadOnlyCollection<int> diskNumbers, Action<DeviceChange> onRemoved);
}
