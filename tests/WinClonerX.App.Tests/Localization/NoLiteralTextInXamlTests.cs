using System.IO;
using System.Xml;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace WinClonerX.App.Tests.Localization;

public sealed class NoLiteralTextInXamlTests
{
    private static readonly string[] CheckedAttributes =
    [
        "Text",
        "Content",
        "Header",
        "Title",
        "ToolTip",
        "AutomationProperties.Name",
    ];

    [Fact]
    public void FindsAppXamlFiles()
    {
        FindAppXamlFiles().ShouldNotBeEmpty();
    }

    [Fact]
    public void XamlFilesHaveNoLiteralUiText()
    {
        var repoRoot = FindRepoRoot();
        var violations = FindAppXamlFiles()
            .SelectMany(file => FindLiteralUiText(Path.GetRelativePath(repoRoot, file), XDocument.Load(file, LoadOptions.SetLineInfo)))
            .ToList();

        violations.ShouldBeEmpty(string.Join(Environment.NewLine, violations));
    }

    [Theory]
    [InlineData("<Button Content=\"Save\" />", 1)]
    [InlineData("<Button Header=\"Step\" ToolTip=\"Help\" />", 2)]
    [InlineData("<Button AutomationProperties.Name=\"Close\" />", 1)]
    [InlineData("<Window Title=\"WinClonerX\" />", 1)]
    [InlineData("<TextBlock Text=\"Hello\" />", 1)]
    [InlineData("<Button Content=\"{loc:Loc Common_Next}\" />", 0)]
    [InlineData("<Button Content=\"\" Text=\"   \" />", 0)]
    [InlineData("<Button Width=\"Save\" Name=\"Save\" />", 0)]
    [InlineData("<TextBlock>Hello</TextBlock>", 1)]
    [InlineData("<TextBlock><Run>Hello</Run></TextBlock>", 1)]
    [InlineData("<TextBlock>\n    </TextBlock>", 0)]
    [InlineData("<Grid>\n\n    </Grid>", 0)]
    public void DetectsLiteralUiTextInSnippets(string xaml, int expectedViolations)
    {
        var doc = XDocument.Parse(xaml, LoadOptions.SetLineInfo);

        FindLiteralUiText("snippet.xaml", doc).Count.ShouldBe(expectedViolations);
    }

    private static List<string> FindLiteralUiText(string fileName, XDocument doc)
    {
        var violations = new List<string>();

        foreach (var element in doc.Descendants())
        {
            var line = ((IXmlLineInfo)element).LineNumber;
            var location = $"{fileName}:{line}";

            foreach (var attribute in element.Attributes())
            {
                if (attribute.Name.NamespaceName.Length != 0 || !CheckedAttributes.Contains(attribute.Name.LocalName))
                {
                    continue;
                }

                var value = attribute.Value.Trim();
                if (value.Length > 0 && !value.StartsWith('{'))
                {
                    violations.Add($"{location}: {element.Name.LocalName} {attribute.Name.LocalName}=\"{attribute.Value}\" is literal text; use {{loc:Loc <Key>}}.");
                }
            }

            foreach (var text in element.Nodes().OfType<XText>().Where(node => !string.IsNullOrWhiteSpace(node.Value)))
            {
                violations.Add($"{location}: <{element.Name.LocalName}> contains literal text \"{text.Value.Trim()}\"; use an attribute with {{loc:Loc <Key>}}.");
            }
        }

        return violations;
    }

    private static IEnumerable<string> FindAppXamlFiles()
    {
        var appDir = Path.Combine(FindRepoRoot(), "src", "WinClonerX.App");

        return Directory.EnumerateFiles(appDir, "*.xaml", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(Path.GetRelativePath(appDir, file)))
            .Order(StringComparer.Ordinal);
    }

    private static bool IsBuildOutput(string relativePath) =>
        relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj");

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
