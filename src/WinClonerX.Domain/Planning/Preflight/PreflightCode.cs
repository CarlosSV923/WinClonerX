namespace WinClonerX.Domain.Planning;

/// <summary>Código de una comprobación previa. La interfaz lo traduce a un texto localizado con sus datos.</summary>
public enum PreflightCode
{
    FirmwareNotUefi,
    SourceNotGpt,
    UnsupportedDiskKind,
    MultipleWindows,
    TargetIsSystem,
    TargetInUse,
    TargetTooSmall,
    SectorSizeMismatch,
    ShrinkInsufficient,
    SnapshotUnavailable,
    SnapshotSpaceLow,
    BitLockerOn,
    OnBattery,
    TargetNotEmpty,
}
