using System.IO;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace WinClonerX.App.Tests.Localization;

public sealed class ScrollViewerStyleTests
{
    private const string PresentationNamespace = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private const string SharedStyle = "{StaticResource ScrollViewer.Page}";

    [Fact]
    public void EveryViewScrollViewerUsesTheSharedPageStyle()
    {
        var viewFiles = Directory.GetFiles(Path.Combine(FindRepoRoot(), "src", "WinClonerX.App", "Views"), "*.xaml");
        var scrollViewers = viewFiles
            .SelectMany(path => XDocument.Load(path).Descendants(XName.Get("ScrollViewer", PresentationNamespace))
                .Select(element => (Path: path, Element: element)))
            .ToArray();

        scrollViewers.ShouldNotBeEmpty();
        var unstyledViewers = scrollViewers
            .Where(viewer => viewer.Element.Attribute("Style")?.Value != SharedStyle)
            .Select(viewer => Path.GetFileName(viewer.Path))
            .ToArray();

        unstyledViewers.ShouldBeEmpty();
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
