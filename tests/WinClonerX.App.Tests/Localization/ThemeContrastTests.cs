using System.IO;
using System.Xml.Linq;
using Xunit;

namespace WinClonerX.App.Tests.Localization;

public sealed class ThemeContrastTests
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string White = "#FFFFFF";

    private static readonly (string Foreground, string Background, double Minimum)[] RequiredPairs =
    [
        ("Brush.TextPrimary", "Brush.Surface", 4.5),
        ("Brush.TextPrimary", "Brush.Background", 4.5),
        ("Brush.TextSecondary", "Brush.Surface", 4.5),
        ("Brush.TextSecondary", "Brush.Background", 4.5),
        ("Brush.TextDisabled", "Brush.Surface", 4.5),
        ("Brush.TextDisabled", "Brush.Background", 4.5),
        ("Brush.RailText", "Brush.Rail", 4.5),
        ("Brush.RailText", "Brush.RailMuted", 4.5),
        ("Brush.RailMuted", "Brush.Rail", 3),
        ("Brush.Accent", "Brush.Rail", 3),
        ("Brush.RailText", "Brush.Accent", 4.5),
        ("Brush.RailText", "Brush.Danger", 4.5),
        (White, "Brush.AccentDisabled", 4.5),
        (White, "Brush.DangerDisabled", 4.5),
        (White, "Brush.Accent", 4.5),
        (White, "Brush.AccentHover", 4.5),
        (White, "Brush.Danger", 4.5),
        (White, "Brush.DangerHover", 4.5),
        (White, "Brush.Success", 4.5),
        ("Brush.Danger", "Brush.Surface", 4.5),
        ("Brush.Danger", "Brush.Background", 4.5),
        ("Brush.Focus", "Brush.Surface", 3),
        ("Brush.Focus", "Brush.Background", 3),
        ("Brush.Border", "Brush.Surface", 3),
        ("Brush.Border", "Brush.Background", 3),
    ];

    [Fact]
    public void ThemeTextAndComponentPairsMeetWcagContrastThresholds()
    {
        var colors = ReadThemeColors();
        foreach (var (foregroundKey, backgroundKey, minimum) in RequiredPairs)
        {
            var foreground = Resolve(foregroundKey, colors);
            var background = Resolve(backgroundKey, colors);
            var ratio = ContrastRatio(foreground, background);

            Assert.True(ratio >= minimum,
                $"{foregroundKey} ({foreground}) on {backgroundKey} ({background}) has contrast {ratio:F2}:1; needs {minimum:F1}:1.");
        }
    }

    private static Dictionary<string, string> ReadThemeColors()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "WinClonerX.App", "Themes", "WinClonerXTheme.xaml");
        var document = XDocument.Load(path);
        return document.Root!.Elements()
            .Where(element => element.Name.LocalName == "SolidColorBrush")
            .ToDictionary(
                element => element.Attribute(XName.Get("Key", XamlNamespace))!.Value,
                element => element.Attribute("Color")!.Value,
                StringComparer.Ordinal);
    }

    private static string Resolve(string key, Dictionary<string, string> colors) =>
        key.StartsWith('#') ? key : colors[key];

    private static double ContrastRatio(string foreground, string background)
    {
        var foregroundLuminance = RelativeLuminance(foreground);
        var backgroundLuminance = RelativeLuminance(background);
        return (Math.Max(foregroundLuminance, backgroundLuminance) + 0.05)
            / (Math.Min(foregroundLuminance, backgroundLuminance) + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        var red = LinearComponent(color, 1);
        var green = LinearComponent(color, 3);
        var blue = LinearComponent(color, 5);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    private static double LinearComponent(string color, int start)
    {
        var component = Convert.ToInt32(color.Substring(start, 2), 16) / 255d;
        return component <= 0.04045
            ? component / 12.92
            : Math.Pow((component + 0.055) / 1.055, 2.4);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WinClonerX.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException($"WinClonerX.slnx was not found above '{AppContext.BaseDirectory}'.");
    }
}
