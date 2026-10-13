using System.Windows.Markup;

namespace WinCopy.App.Localization;

/// <summary>
/// Markup extension for localized text in XAML: <c>{loc:Loc App_Title}</c>.
/// Map the namespace with <c>xmlns:loc="clr-namespace:WinCopy.App.Localization"</c>.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension : MarkupExtension
{
    public LocExtension(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        Key = key;
    }

    /// <summary>The resource key to look up.</summary>
    public string Key { get; }

    public override object ProvideValue(IServiceProvider serviceProvider) => Loc.Get(Key);
}
