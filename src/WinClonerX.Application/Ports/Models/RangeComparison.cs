namespace WinClonerX.Application.Ports.Models;

/// <summary>Resultado de verificar los chunks copiados contra sus digests.</summary>
public sealed record RangeComparison(
    long BytesCompared,
    long DifferingBytes,
    IReadOnlyList<long> FirstDifferenceOffsets,
    long DifferingChunks,
    IReadOnlyList<ChunkMismatchSample>? MismatchSamples = null);
