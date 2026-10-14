namespace WinCopy.Domain.Execution;

/// <summary>
/// Motivo de un fallo: código estable para la interfaz, fase en la que ocurrió y detalle técnico (solo para el registro).
/// </summary>
public sealed record ExecutionFailure(string Code, ExecutionPhase Phase, string TechnicalDetail);
