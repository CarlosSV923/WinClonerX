using WinClonerX.Application.Ports;

namespace WinClonerX.Infrastructure.Windows;

/// <summary>Reloj del sistema: el instante UTC actual de <see cref="DateTimeOffset.UtcNow"/>.</summary>
internal sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
