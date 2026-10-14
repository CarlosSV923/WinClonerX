using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Domain.Planning;

/// <summary>
/// Hechos del entorno que la capa de aplicación obtiene y entrega al dominio para las comprobaciones previas.
/// Los valores ausentes en los diccionarios se tratan como "no comprobado" y bloquean la comprobación que dependa de ellos.
/// </summary>
public sealed record PlatformFacts(
    IReadOnlyDictionary<PartitionGuid, EncryptionState> Encryption,
    bool OnBattery,
    int? BatteryPercent,
    bool SnapshotAvailable,
    ByteSize SnapshotFreeSpace,
    ByteSize? MinimumWindowsSize,
    IReadOnlyDictionary<DiskId, bool> TargetInUse);
