using System.Globalization;
using System.Windows.Data;
using WinCopy.App.Localization;

namespace WinCopy.App.Converters;

/// <summary>
/// Turns a resource key bound to a view (for example a wizard step title key) into its localized text.
/// </summary>
public sealed class LocKeyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is string key && key.Length > 0 ? Loc.Get(key) : string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException("Localized text cannot be converted back to a resource key.");
    }
}
