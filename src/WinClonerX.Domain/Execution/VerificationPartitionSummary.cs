using WinClonerX.Domain.Inventory;

namespace WinClonerX.Domain.Execution;

/// <summary>Resumen de bytes comparados y distintos en una partición de la verificación completa.</summary>
public sealed record VerificationPartitionSummary(PartitionRole Role, long BytesCompared, long DifferingBytes);
