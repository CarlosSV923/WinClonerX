using WinCopy.Application.Errors;
using WinCopy.Domain.Inventory;
using WinCopy.Infrastructure.Windows.Storage;

namespace WinCopy.Infrastructure.Windows.Platform;

/// <summary>
/// Shared BitLocker helper. It turns the <c>Win32_EncryptableVolume</c> rows into one <see cref="EncryptionState"/> per
/// volume, which the inventory reader and the facts provider use.
/// </summary>
/// <remarks>
/// <para>Mapping (<see cref="MapStatus"/>):</para>
/// <list type="bullet">
/// <item>ProtectionStatus 1 → <see cref="EncryptionState.BitLockerOn"/>.</item>
/// <item>ProtectionStatus 0 and ConversionStatus ≠ 0 (not fully decrypted) → <see cref="EncryptionState.BitLockerSuspended"/>.</item>
/// <item>ProtectionStatus 0 and ConversionStatus 0 → <see cref="EncryptionState.None"/>.</item>
/// <item>Anything else, including a missing value → <see cref="EncryptionState.Unknown"/>.</item>
/// </list>
/// <para>
/// If the BitLocker namespace does not exist, every volume is <see cref="EncryptionState.None"/> and a warning is raised.
/// BitLocker is a warning in the planner, not a blocker, so the clone is not stopped by that. A volume that is not listed
/// while the namespace answers is <see cref="EncryptionState.Unknown"/>. Any other failure (for example access denied)
/// is not swallowed.
/// </para>
/// </remarks>
internal sealed class BitLockerQuery(IBitLockerQuery source, Action<string>? warn = null)
{
    private const uint ProtectionOff = 0;
    private const uint ProtectionOn = 1;
    private const uint ConversionFullyDecrypted = 0;

    private readonly IBitLockerQuery _source = source ?? throw new ArgumentNullException(nameof(source));
    private readonly Action<string> _warn = warn ?? (static _ => { });

    /// <summary>
    /// The state of each requested volume, keyed by the normalized volume GUID path (see <see cref="VolumePath.Normalize"/>).
    /// Every requested path is in the result.
    /// </summary>
    public IReadOnlyDictionary<string, EncryptionState> GetStates(IEnumerable<string> volumeGuidPaths)
    {
        ArgumentNullException.ThrowIfNull(volumeGuidPaths);

        string[] paths = volumeGuidPaths.Select(VolumePath.Normalize).Distinct(StringComparer.Ordinal).ToArray();

        IReadOnlyList<BitLockerVolumeRecord> rows;
        try
        {
            rows = _source.GetVolumes();
        }
        catch (PlatformException ex) when (ex.Code == PlatformErrorCode.DeviceNotFound)
        {
            _warn($"BitLocker is not available on this machine; volumes are reported as not encrypted ({ex.TechnicalDetail}).");
            return paths.ToDictionary(path => path, _ => EncryptionState.None, StringComparer.Ordinal);
        }

        var byPath = new Dictionary<string, EncryptionState>(StringComparer.Ordinal);
        foreach (BitLockerVolumeRecord row in rows)
        {
            if (string.IsNullOrWhiteSpace(row.DeviceId))
            {
                continue;
            }

            string key = VolumePath.Normalize(row.DeviceId);

            // Two rows for one volume cannot be told apart, so the result is Unknown.
            byPath[key] = byPath.ContainsKey(key)
                ? EncryptionState.Unknown
                : MapStatus(row.ProtectionStatus, row.ConversionStatus);
        }

        return paths.ToDictionary(
            path => path,
            path => byPath.TryGetValue(path, out EncryptionState state) ? state : EncryptionState.Unknown,
            StringComparer.Ordinal);
    }

    /// <summary>Maps the BitLocker status codes to an <see cref="EncryptionState"/>. Pure, so it is unit-tested.</summary>
    internal static EncryptionState MapStatus(uint? protectionStatus, uint? conversionStatus)
    {
        if (protectionStatus == ProtectionOn)
        {
            return EncryptionState.BitLockerOn;
        }

        if (protectionStatus == ProtectionOff)
        {
            return conversionStatus switch
            {
                null => EncryptionState.Unknown,
                ConversionFullyDecrypted => EncryptionState.None,
                _ => EncryptionState.BitLockerSuspended,
            };
        }

        return EncryptionState.Unknown;
    }
}
