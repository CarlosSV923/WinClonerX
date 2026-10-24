using System.Windows.Media;

namespace WinClonerX.App.Themes;

/// <summary>
/// Typefaces used by the WinClonerX theme. Each value is a fallback list tried left to right.
/// Typeface names are configuration, not UI text, so they live here rather than in resx files.
/// </summary>
public static class WinClonerXFonts
{
    /// <summary>Text font: Segoe UI Variable when installed, otherwise Segoe UI.</summary>
    public static FontFamily Text { get; } = new("Segoe UI Variable, Segoe UI");

    /// <summary>Icon font for glyphs such as the completed-step check.</summary>
    public static FontFamily Icons { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");
}
