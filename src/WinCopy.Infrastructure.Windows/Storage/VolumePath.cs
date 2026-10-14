using System.Text.RegularExpressions;

namespace WinCopy.Infrastructure.Windows.Storage;

/// <summary>Comparison and device-path helpers for volume GUID paths such as <c>\\?\Volume{...}\</c>.</summary>
internal static partial class VolumePath
{
    private const char Separator = '\\';

    /// <summary>Returns true for a volume GUID path with the exact shape <c>\\?\Volume{GUID}\</c>.</summary>
    public static bool IsVolumeGuidPath(string? path) => path is not null && VolumeGuidPattern().IsMatch(path);

    /// <summary>Canonical form for comparisons: no trailing backslash, upper case.</summary>
    public static string Normalize(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        return path.TrimEnd(Separator).ToUpperInvariant();
    }

    /// <summary>Compares two volume paths, ignoring a trailing backslash and case. Null never matches.</summary>
    public static bool IsSameVolume(string? left, string? right) =>
        left is not null
        && right is not null
        && string.Equals(Normalize(left), Normalize(right), StringComparison.Ordinal);

    /// <summary>Device path for <c>CreateFileW</c>: the volume GUID path without its trailing backslash.</summary>
    public static string ToDevicePath(string volumeGuidPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(volumeGuidPath);

        return volumeGuidPath.TrimEnd(Separator);
    }

    [GeneratedRegex(@"^\\\\\?\\Volume\{[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}\}\\$",
        RegexOptions.CultureInvariant)]
    private static partial Regex VolumeGuidPattern();
}
