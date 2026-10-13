using WinCopy.Domain.Inventory;
using WinCopy.Domain.Shared;

namespace WinCopy.Application.Ports.Models;

/// <summary>Hechos del entorno que la aplicación obtiene y entrega al dominio para las comprobaciones previas.</summary>
public sealed record PlatformFacts(
    FirmwareType Firmware,
    IReadOnlyDictionary<PartitionGuid, EncryptionState> Encryption,
    PowerStatus Power,
    bool SnapshotAvailable,
    ByteSize SnapshotFreeSpace,
    ByteSize? MinimumWindowsSize,
    bool? TargetInUse);
