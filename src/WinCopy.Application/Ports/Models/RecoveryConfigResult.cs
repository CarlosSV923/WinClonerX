namespace WinCopy.Application.Ports.Models;

/// <summary>Resultado de configurar el entorno de recuperación. Detail es un texto técnico para el registro, si lo hay.</summary>
public sealed record RecoveryConfigResult(bool Configured, string? Detail);
