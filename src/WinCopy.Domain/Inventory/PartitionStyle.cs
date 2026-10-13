namespace WinCopy.Domain.Inventory;

/// <summary>Esquema de tabla de particiones de un disco.</summary>
public enum PartitionStyle
{
    Gpt,
    Mbr,
    Raw,
}
