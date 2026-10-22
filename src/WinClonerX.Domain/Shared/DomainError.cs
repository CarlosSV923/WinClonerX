namespace WinClonerX.Domain.Shared;

/// <summary>Error de dominio con un código estable y un detalle opcional para el registro.</summary>
public sealed record DomainError(string Code, string? Detail = null);
