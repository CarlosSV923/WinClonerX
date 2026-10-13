namespace WinCopy.Application.Ports.Models;

/// <summary>Estado de energía del equipo: si funciona con batería y el porcentaje restante, si se conoce.</summary>
public sealed record PowerStatus(bool OnBattery, int? BatteryPercent);
