namespace WinCopy.Domain.Inventory;

/// <summary>Tipo de sistema de archivos de un volumen.</summary>
public enum FileSystemKind
{
    Ntfs,
    Fat32,
    ExFat,
    ReFs,
    Raw,
    Other,
}
