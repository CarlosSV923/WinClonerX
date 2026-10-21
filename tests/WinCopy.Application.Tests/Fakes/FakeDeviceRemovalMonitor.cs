using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>
/// Fake de IDeviceRemovalMonitor: las retiradas se provocan a mano con <see cref="Remove"/> o <see cref="Raise"/>. Solo avisa de
/// los discos que la vigilancia activa cubre, como haría el adaptador real.
/// </summary>
public sealed class FakeDeviceRemovalMonitor : FakeBase, IDeviceRemovalMonitor
{
    private readonly object _gate = new();
    private readonly List<Registration> _watches = [];

    /// <summary>Números de disco de la última vigilancia iniciada, o vacío si no ha habido ninguna.</summary>
    public IReadOnlyList<int> LastWatchedDisks { get; private set; } = [];

    /// <summary>Número de vigilancias iniciadas y todavía no liberadas.</summary>
    public int ActiveWatches
    {
        get
        {
            lock (_gate)
            {
                return _watches.Count;
            }
        }
    }

    /// <summary>Número de vigilancias iniciadas en total.</summary>
    public int WatchesStarted { get; private set; }

    /// <summary>Inicia una vigilancia. Con <see cref="FakeBase.FailOn"/> de "Watch" lanza la excepción configurada.</summary>
    public IDisposable Watch(IReadOnlyCollection<int> diskNumbers, Action<DeviceChange> onRemoved)
    {
        ArgumentNullException.ThrowIfNull(diskNumbers);
        ArgumentNullException.ThrowIfNull(onRemoved);

        Enter(nameof(Watch));

        var registration = new Registration(this, diskNumbers.ToArray(), onRemoved);
        lock (_gate)
        {
            _watches.Add(registration);
        }

        LastWatchedDisks = registration.DiskNumbers;
        WatchesStarted++;
        return registration;
    }

    /// <summary>Provoca la retirada del disco indicado y avisa a las vigilancias que lo cubren.</summary>
    public void Remove(int diskNumber) => Raise(new DeviceChange(DeviceChangeKind.Removed, diskNumber));

    /// <summary>Publica un cambio de dispositivo a las vigilancias activas que cubren su disco.</summary>
    public void Raise(DeviceChange change)
    {
        Calls.Add(nameof(Raise));

        Registration[] active;
        lock (_gate)
        {
            active = [.. _watches];
        }

        foreach (var registration in active)
        {
            if (change.DiskNumber is { } number && registration.DiskNumbers.Contains(number))
            {
                registration.OnRemoved(change);
            }
        }
    }

    private void Stop(Registration registration)
    {
        Calls.Add("Dispose");
        lock (_gate)
        {
            _watches.Remove(registration);
        }
    }

    private sealed class Registration(FakeDeviceRemovalMonitor owner, int[] diskNumbers, Action<DeviceChange> onRemoved) : IDisposable
    {
        private bool _disposed;

        public int[] DiskNumbers { get; } = diskNumbers;

        public Action<DeviceChange> OnRemoved { get; } = onRemoved;

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                owner.Stop(this);
            }
        }
    }
}
