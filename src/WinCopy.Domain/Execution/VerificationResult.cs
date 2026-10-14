namespace WinCopy.Domain.Execution;

/// <summary>
/// Resultado de una verificación. La estructural informa de los problemas encontrados. La completa añade el número de bytes
/// distintos y los primeros desplazamientos donde difieren.
/// </summary>
public sealed record VerificationResult(
    bool Passed,
    IReadOnlyList<string> Issues,
    long? DifferingBytes = null,
    IReadOnlyList<long>? FirstDifferenceOffsets = null);
