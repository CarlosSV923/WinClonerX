namespace WinCopy.Domain.Inventory;

/// <summary>Tipo de arranque del equipo. En la versión 1 solo se soporta UEFI.</summary>
public enum FirmwareType
{
    Uefi,
    Bios,
    Unknown,
}
