namespace WinClonerX.Domain.Execution;

/// <summary>
/// Avance medido de la copia: bytes confirmados, total, rendimiento y tiempo restante estimado si ya se puede calcular (R17).
/// </summary>
public sealed record CloneProgress(long BytesDone, long BytesTotal, double ThroughputBytesPerSecond, TimeSpan? Eta)
{
    /// <summary>Bytes ya confirmados; entre 0 y el total.</summary>
    public long BytesDone { get; } = BytesDone >= 0 && BytesDone <= BytesTotal
        ? BytesDone
        : throw new ArgumentOutOfRangeException(nameof(BytesDone), BytesDone, "Los bytes hechos deben estar entre 0 y el total.");

    /// <summary>Bytes totales a copiar; no puede ser negativo.</summary>
    public long BytesTotal { get; } = BytesTotal >= 0
        ? BytesTotal
        : throw new ArgumentOutOfRangeException(nameof(BytesTotal), BytesTotal, "El total no puede ser negativo.");

    /// <summary>Rendimiento medido en bytes por segundo; no puede ser negativo.</summary>
    public double ThroughputBytesPerSecond { get; } = ThroughputBytesPerSecond >= 0 && double.IsFinite(ThroughputBytesPerSecond)
        ? ThroughputBytesPerSecond
        : throw new ArgumentOutOfRangeException(nameof(ThroughputBytesPerSecond), ThroughputBytesPerSecond, "El rendimiento debe ser un número finito no negativo.");

    /// <summary>Porcentaje completado, de 0 a 100. Un total de cero se considera completo.</summary>
    public double Percent => BytesTotal == 0 ? 100.0 : Math.Min(100.0, BytesDone * 100.0 / BytesTotal);
}
