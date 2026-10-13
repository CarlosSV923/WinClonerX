namespace WinCopy.Domain.Inventory;

/// <summary>Bus por el que está conectado un disco.</summary>
public enum BusKind
{
    Nvme,
    Sata,
    Sas,
    Usb,
    FileBackedVirtual,
    Spaces,
    Raid,
    Other,
}
