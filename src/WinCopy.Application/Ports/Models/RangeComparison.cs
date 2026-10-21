namespace WinCopy.Application.Ports.Models;

/// <summary>
/// Resultado de comparar los tramos ocupados de una partición con su copia en el destino (verificación completa, FR-022a).
/// <see cref="FirstDifferenceOffsets"/> son desplazamientos relativos al inicio del volumen o de la partición de origen, en orden,
/// como mucho diez.
/// </summary>
public sealed record RangeComparison(long BytesCompared, long DifferingBytes, IReadOnlyList<long> FirstDifferenceOffsets);
