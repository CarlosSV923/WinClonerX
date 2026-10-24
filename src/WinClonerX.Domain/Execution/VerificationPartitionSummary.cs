using WinClonerX.Domain.Inventory;

namespace WinClonerX.Domain.Execution;

/// <summary>Resumen de chunks distintos y bytes verificados en una partición.</summary>
public sealed record VerificationPartitionSummary(PartitionRole Role, long BytesCompared, long DifferingBytes, long DifferingChunks = 0);
