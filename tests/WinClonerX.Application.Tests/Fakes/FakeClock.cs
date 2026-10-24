using WinClonerX.Application.Ports;

namespace WinClonerX.Application.Tests.Fakes;

/// <summary>Fake de IClock con instante configurable.</summary>
public sealed class FakeClock : FakeBase, IClock
{
    private DateTimeOffset _utcNow = FakeDefaults.Now;

    /// <summary>Instante que devuelve UtcNow. Cada lectura queda registrada en Calls.</summary>
    public DateTimeOffset UtcNow
    {
        get
        {
            Calls.Add(nameof(UtcNow));
            return _utcNow;
        }

        set => _utcNow = value;
    }
}
