using System.Globalization;
using System.Management;
using WinClonerX.Application.Errors;

namespace WinClonerX.Infrastructure.Windows.Platform;

/// <summary>
/// WMI implementation of <see cref="IBitLockerQuery"/>: <c>Win32_EncryptableVolume</c> in
/// <c>root\cimv2\Security\MicrosoftVolumeEncryption</c>. Querying it needs administrator rights.
/// </summary>
internal sealed class WmiBitLockerQuery : IBitLockerQuery
{
    private const string BitLockerScope = @"root\cimv2\Security\MicrosoftVolumeEncryption";
    private const string Query = "SELECT DeviceID, ProtectionStatus, ConversionStatus FROM Win32_EncryptableVolume";

    public IReadOnlyList<BitLockerVolumeRecord> GetVolumes()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(BitLockerScope, Query);
            using ManagementObjectCollection results = searcher.Get();

            var rows = new List<BitLockerVolumeRecord>();
            foreach (ManagementBaseObject item in results)
            {
                using (item)
                {
                    rows.Add(new BitLockerVolumeRecord(
                        Convert.ToString(item["DeviceID"], CultureInfo.InvariantCulture) ?? string.Empty,
                        ReadOptional(item["ProtectionStatus"]),
                        ReadOptional(item["ConversionStatus"])));
                }
            }

            return rows;
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new PlatformException(PlatformErrorCode.AccessDenied, "WMI denied the BitLocker query.", ex);
        }
        catch (ManagementException ex) when (ex.ErrorCode is ManagementStatus.InvalidNamespace
                                                 or ManagementStatus.InvalidClass
                                                 or ManagementStatus.NotFound)
        {
            throw new PlatformException(
                PlatformErrorCode.DeviceNotFound,
                $"BitLocker WMI namespace is not available: {ex.Message}",
                ex);
        }
        catch (ManagementException ex)
        {
            var code = ex.ErrorCode == ManagementStatus.AccessDenied
                ? PlatformErrorCode.AccessDenied
                : PlatformErrorCode.Unknown;

            throw new PlatformException(code, $"BitLocker WMI query failed: {ex.Message}", ex);
        }
    }

    private static uint? ReadOptional(object? value) =>
        value is null ? null : Convert.ToUInt32(value, CultureInfo.InvariantCulture);
}
