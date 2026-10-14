using System.Globalization;
using WinCopy.App.Localization;
using WinCopy.Domain.Shared;

namespace WinCopy.App.Presentation;

/// <summary>
/// Formats byte counts for people: the largest unit that keeps the number at or above one, with up to one decimal.
/// Units are binary (1 GB = 1024 MB), the same way Windows reports disk sizes.
/// </summary>
public static class SizeText
{
    private const long Kib = 1L << 10;
    private const long Mib = 1L << 20;
    private const long Gib = 1L << 30;
    private const long Tib = 1L << 40;

    /// <summary>Formats a size using the current culture for the number and the current UI culture for the unit.</summary>
    public static string Format(ByteSize size)
    {
        var bytes = size.Bytes;

        if (bytes >= Tib)
        {
            return Loc.Format("Size_Tb", Number(bytes / (double)Tib));
        }

        if (bytes >= Gib)
        {
            return Loc.Format("Size_Gb", Number(bytes / (double)Gib));
        }

        if (bytes >= Mib)
        {
            return Loc.Format("Size_Mb", Number(bytes / (double)Mib));
        }

        if (bytes >= Kib)
        {
            return Loc.Format("Size_Kb", Number(bytes / (double)Kib));
        }

        return Loc.Format("Size_B", bytes.ToString(CultureInfo.CurrentCulture));
    }

    private static string Number(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
}
