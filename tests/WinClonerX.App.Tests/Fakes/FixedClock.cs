using WinClonerX.Application.Ports;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>IClock fake that always reports the same instant, so names built from the time are predictable.</summary>
internal sealed class FixedClock : IClock
{
    /// <summary>Instant returned by UtcNow.</summary>
    public DateTimeOffset UtcNow { get; set; } = new(2026, 10, 20, 10, 15, 30, TimeSpan.Zero);
}
