namespace WinCopy.Domain.Inventory;

/// <summary>Estado de cifrado de un volumen.</summary>
public enum EncryptionState
{
    None,
    BitLockerOn,
    BitLockerSuspended,
    Unknown,
}
