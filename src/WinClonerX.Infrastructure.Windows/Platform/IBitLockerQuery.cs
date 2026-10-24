using WinClonerX.Application.Errors;

namespace WinClonerX.Infrastructure.Windows.Platform;

/// <summary>One <c>Win32_EncryptableVolume</c> row. A property the WMI class did not return is null.</summary>
internal sealed record BitLockerVolumeRecord(string DeviceId, uint? ProtectionStatus, uint? ConversionStatus);

/// <summary>Reads the BitLocker rows of the encryptable volumes. Read-only.</summary>
internal interface IBitLockerQuery
{
    /// <summary>
    /// One row per encryptable volume. Throws <see cref="PlatformException"/> with <see cref="PlatformErrorCode.DeviceNotFound"/>
    /// when the BitLocker WMI namespace does not exist.
    /// </summary>
    IReadOnlyList<BitLockerVolumeRecord> GetVolumes();
}
