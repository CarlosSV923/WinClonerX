namespace WinCopy.Application.Ports.Models;

/// <summary>Cambio de dispositivo detectado: un disco ha llegado o se ha retirado. El número de disco puede no conocerse.</summary>
public sealed record DeviceChange(DeviceChangeKind Kind, int? DiskNumber);
