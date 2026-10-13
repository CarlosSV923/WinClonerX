namespace WinCopy.Domain.Inventory;

/// <summary>Función de una partición, derivada de su tipo GPT y de su volumen.</summary>
public enum PartitionRole
{
    EfiSystem,
    MsReserved,
    Windows,
    Recovery,
    Data,
    Unsupported,
}
