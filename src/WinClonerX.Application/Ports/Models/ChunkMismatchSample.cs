namespace WinClonerX.Application.Ports.Models;

/// <summary>Muestra diagnóstica de un chunk cuyo digest no coincidió en el destino.</summary>
public sealed record ChunkMismatchSample(long TargetOffset, int Length, bool TargetStable);
