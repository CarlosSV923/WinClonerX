using WinCopy.Application.Ports;
using WinCopy.Application.Ports.Models;

namespace WinCopy.Application.Tests.Fakes;

/// <summary>Fake de IPowerManager: IsSleepBlocked es verdadero mientras haya algún PreventSleep sin liberar.</summary>
public sealed class FakePowerManager : FakeBase, IPowerManager
{
    /// <summary>Estado de energía que devuelve GetStatusAsync.</summary>
    public PowerStatus Status { get; set; } = new(false, 100);

    /// <summary>Número de bloqueos de suspensión activos.</summary>
    public int ActiveSleepBlocks { get; private set; }

    /// <summary>Número de adquisiciones de PreventSleep que tuvieron éxito.</summary>
    public int Acquisitions { get; private set; }

    /// <summary>Número de liberaciones de bloqueos de suspensión.</summary>
    public int Releases { get; private set; }

    /// <summary>Indica si la suspensión está bloqueada en este momento.</summary>
    public bool IsSleepBlocked => ActiveSleepBlocks > 0;

    /// <summary>Bloquea la suspensión hasta que el objeto devuelto se libere; liberarlo dos veces no cuenta dos veces.</summary>
    public IDisposable PreventSleep()
    {
        Enter(nameof(PreventSleep));
        Acquisitions++;
        ActiveSleepBlocks++;
        return new SleepBlock(this);
    }

    /// <summary>Devuelve el estado de energía configurado.</summary>
    public Task<PowerStatus> GetStatusAsync(CancellationToken ct) =>
        Reply(nameof(GetStatusAsync), () => Status, ct);

    private sealed class SleepBlock(FakePowerManager owner) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                owner.ActiveSleepBlocks--;
                owner.Releases++;
                owner.Calls.Add("Release");
            }
        }
    }
}
