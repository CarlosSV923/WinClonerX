using System.IO;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace WinClonerX.App.Tests.Localization;

// The primary, secondary and danger buttons must render with the same size. Their metrics live only in Button.Base; the
// variants may change colors and font weight, but never the size-related setters.
public sealed class ButtonMetricsTests
{
    private const string XamlNamespace = "http://schemas.microsoft.com/winfx/2006/xaml";
    private const string BaseStyle = "Button.Base";

    private static readonly string[] Variants = ["Button.Primary", "Button.Secondary", "Button.Danger"];
    private static readonly string[] SizeProperties = ["Padding", "MinHeight", "MinWidth", "FontSize", "BorderThickness", "Margin"];

    [Theory]
    [InlineData("Button.Primary")]
    [InlineData("Button.Secondary")]
    [InlineData("Button.Danger")]
    public void EveryButtonVariantIsBasedOnTheSharedMetrics(string key)
    {
        var style = Styles()[key];

        style.Attribute("BasedOn").ShouldNotBeNull().Value.ShouldBe("{StaticResource " + BaseStyle + "}");
    }

    [Theory]
    [InlineData("Button.Primary")]
    [InlineData("Button.Secondary")]
    [InlineData("Button.Danger")]
    public void NoButtonVariantOverridesASizeSetter(string key)
    {
        var overridden = SetterProperties(Styles()[key]).Intersect(SizeProperties).ToArray();

        overridden.ShouldBeEmpty();
    }

    [Theory]
    [InlineData("Button.Primary")]
    [InlineData("Button.Secondary")]
    [InlineData("Button.Danger")]
    public void TheButtonTemplateHonorsTheStyleBorderThickness(string key)
    {
        // A fixed BorderThickness in the template shrinks the painted area of one variant against the others.
        var chrome = Styles()[key].Descendants().First(element => element.Name.LocalName == "Border");

        chrome.Attribute("BorderThickness").ShouldNotBeNull().Value.ShouldBe("{TemplateBinding BorderThickness}");
    }

    [Fact]
    public void TheSharedMetricsDefineEverySizeSetter()
    {
        SizeProperties.Except(SetterProperties(Styles()[BaseStyle])).ShouldBeEmpty();
        Variants.ShouldAllBe(variant => Styles().ContainsKey(variant));
    }

    private static Dictionary<string, XElement> Styles()
    {
        var path = Path.Combine(FindRepoRoot(), "src", "WinClonerX.App", "Themes", "WinClonerXTheme.xaml");
        return XDocument.Load(path).Root!.Elements()
            .Where(element => element.Name.LocalName == "Style" && element.Attribute(XName.Get("Key", XamlNamespace)) is not null)
            .ToDictionary(element => element.Attribute(XName.Get("Key", XamlNamespace))!.Value, StringComparer.Ordinal);
    }

    // Only the style's own setters: the template's trigger setters change colors, not the size.
    private static IEnumerable<string> SetterProperties(XElement style) =>
        style.Elements()
            .Where(element => element.Name.LocalName == "Setter")
            .Select(element => element.Attribute("Property")?.Value ?? string.Empty);

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
