using WinClonerX.Domain.Shared;

namespace WinClonerX.Domain.Inventory;

/// <summary>Sistema de archivos de una partición, con su capacidad, espacio ocupado y cifrado.</summary>
public sealed record Volume(
    FileSystemKind FileSystem,
    ByteSize Capacity,
    ByteSize Used,
    ByteSize? ClusterSize,
    EncryptionState Encryption,
    bool IsWindowsVolume,
    bool? InUseByOthers)
{
    /// <summary>Espacio ocupado; no puede superar la capacidad.</summary>
    public ByteSize Used { get; } = Used > Capacity
        ? throw new ArgumentException("El espacio ocupado no puede superar la capacidad.", nameof(Used))
        : Used;
}
