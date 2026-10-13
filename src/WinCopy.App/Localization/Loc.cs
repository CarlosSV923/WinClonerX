using System.Globalization;
using System.Resources;

namespace WinCopy.App.Localization;

/// <summary>
/// Looks up UI strings in <c>Resources/Strings.resx</c> (English, neutral) and
/// <c>Resources/Strings.es.resx</c>. The culture is <see cref="CultureInfo.CurrentUICulture"/>;
/// anything without a translation falls back to English.
/// </summary>
public static class Loc
{
    private static readonly ResourceManager ResourceManager =
        new("WinCopy.App.Resources.Strings", typeof(Loc).Assembly);

    /// <summary>
    /// Returns the localized text for <paramref name="key"/>, or <c>[[key]]</c> when the key does not exist.
    /// </summary>
    public static string Get(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        return ResourceManager.GetString(key, CultureInfo.CurrentUICulture) ?? $"[[{key}]]";
    }
}
