namespace WinCopy.Application.Errors;

/// <summary>Código estable y localizable de un error de plataforma. El detalle técnico solo va al registro.</summary>
public enum PlatformErrorCode
{
    Unknown,
    AccessDenied,
    DeviceNotFound,
    DeviceRemoved,
    SourceReadError,
    TargetWriteError,
    SnapshotFailed,
    ShrinkFailed,
    RestoreFailed,
    ExtendFailed,
    MountFailed,
    BootConfigFailed,
    RecoveryConfigFailed,
    RegistryEditFailed,
    VerificationFailed,
    TargetInUse,
    Cancelled,
}
