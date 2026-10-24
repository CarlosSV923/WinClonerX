using WinClonerX.Application.Ports;
using WinClonerX.Domain.Execution;

namespace WinClonerX.Application.UseCases.Execution;

/// <summary>
/// Estima el progreso de la copia (R17). El rendimiento es una media móvil exponencial de 30 s: en cada muestra,
/// <c>alpha = 1 − exp(−Δt/30 s)</c> aplicado al rendimiento instantáneo. Empieza con la estimación del plan. El tiempo restante
/// solo se muestra después del 1 % completado o de 10 s de ejecución.
/// </summary>
public sealed class ProgressEstimator
{
    private static readonly TimeSpan Window = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan EtaDelay = TimeSpan.FromSeconds(10);
    private const double EtaPercentThreshold = 1.0;
    private const double MaxEtaSeconds = 9.2e10;

    private readonly long _totalBytes;
    private readonly IClock _clock;
    private readonly DateTimeOffset _startedAt;
    private DateTimeOffset _lastSampleAt;
    private long _lastSampleBytes;
    private double _bytesPerSecond;

    /// <summary>Crea un estimador para una copia de <paramref name="totalBytes"/> bytes con el rendimiento inicial indicado.</summary>
    public ProgressEstimator(long totalBytes, IClock clock, double initialBytesPerSecond)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (totalBytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalBytes), totalBytes, "El total de bytes no puede ser negativo.");
        }

        if (!double.IsFinite(initialBytesPerSecond) || initialBytesPerSecond < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(initialBytesPerSecond), initialBytesPerSecond, "El rendimiento inicial debe ser finito y no negativo.");
        }

        _totalBytes = totalBytes;
        _clock = clock;
        _startedAt = clock.UtcNow;
        _lastSampleAt = _startedAt;
        _bytesPerSecond = initialBytesPerSecond;
    }

    /// <summary>Registra el avance hasta <paramref name="bytesDone"/> bytes y devuelve el progreso estimado en este instante.</summary>
    public CloneProgress Update(long bytesDone)
    {
        var done = Math.Clamp(bytesDone, 0, _totalBytes);
        var now = _clock.UtcNow;

        var seconds = (now - _lastSampleAt).TotalSeconds;
        if (seconds > 0)
        {
            var instantaneous = Math.Max(0, done - _lastSampleBytes) / seconds;
            var alpha = 1 - Math.Exp(-seconds / Window.TotalSeconds);
            _bytesPerSecond = (alpha * instantaneous) + ((1 - alpha) * _bytesPerSecond);
            _lastSampleAt = now;
            _lastSampleBytes = done;
        }

        return new CloneProgress(done, _totalBytes, _bytesPerSecond, Eta(done, now - _startedAt));
    }

    private TimeSpan? Eta(long done, TimeSpan elapsed)
    {
        var percent = _totalBytes == 0 ? 100.0 : done * 100.0 / _totalBytes;
        if (percent < EtaPercentThreshold && elapsed < EtaDelay)
        {
            return null;
        }

        var remaining = _totalBytes - done;
        if (remaining == 0)
        {
            return TimeSpan.Zero;
        }

        if (_bytesPerSecond <= 0)
        {
            return null;
        }

        var seconds = remaining / _bytesPerSecond;
        return seconds >= MaxEtaSeconds ? TimeSpan.MaxValue : TimeSpan.FromSeconds(seconds);
    }
}
