namespace WinClonerX.Domain.Execution;

/// <summary>Resultado de una compensación. Un fallo se registra y no oculta el error original de la ejecución.</summary>
public sealed record CompensationRecord(CompensationKind Kind, bool Success, string? Detail);
