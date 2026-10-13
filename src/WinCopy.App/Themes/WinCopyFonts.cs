using System.Windows.Media;

namespace WinCopy.App.Themes;

/// <summary>
/// Typefaces used by the WinCopy theme. Each value is a fallback list tried left to right.
/// Typeface names are configuration, not UI text, so they live here rather than in resx files.
/// </summary>
public static class WinCopyFonts
{
    /// <summary>Text font: Segoe UI Variable when installed, otherwise Segoe UI.</summary>
    public static FontFamily Text { get; } = new("Segoe UI Variable, Segoe UI");

    /// <summary>Icon font for glyphs such as the completed-step check.</summary>
    public static FontFamily Icons { get; } = new("Segoe Fluent Icons, Segoe MDL2 Assets");
}
