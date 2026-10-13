using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de IDeviceChangeMonitor: los cambios se publican a mano con Raise.</summary>
public sealed class FakeDeviceChangeMonitor : FakeBase, IDeviceChangeMonitor
{
    private readonly SimpleSubject<DeviceChange> _subject = new();

    /// <summary>Flujo de cambios; los suscriptores reciben lo que se publique con Raise.</summary>
    public IObservable<DeviceChange> Changes => _subject;

    /// <summary>Número de suscriptores activos.</summary>
    public int SubscriberCount => _subject.SubscriberCount;

    /// <summary>Publica un cambio de dispositivo a todos los suscriptores activos.</summary>
    public void Raise(DeviceChange change)
    {
        Calls.Add(nameof(Raise));
        _subject.OnNext(change);
    }
}
