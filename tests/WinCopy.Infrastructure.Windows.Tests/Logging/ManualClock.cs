using WinCopy.Application.Ports;

namespace WinCopy.Infrastructure.Windows.Tests.Logging;

/// <summary>IClock whose instant only moves when the test calls <see cref="Advance"/>.</summary>
public sealed class ManualClock : IClock
{
    private DateTimeOffset _now;

    public ManualClock(DateTimeOffset start)
    {
        _now = start;
    }

    public DateTimeOffset UtcNow => _now;

    public void Advance(TimeSpan by)
    {
        _now += by;
    }
}
