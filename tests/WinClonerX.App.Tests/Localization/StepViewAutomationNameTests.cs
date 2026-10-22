using System.IO;
using System.Xml.Linq;
using Shouldly;
using Xunit;

namespace WinClonerX.App.Tests.Localization;

public sealed class StepViewAutomationNameTests
{
    [Fact]
    public void InteractiveControlsAndDiskItemTemplatesHaveAccessibleNames()
    {
        var failures = new List<string>();
        foreach (var file in Directory.EnumerateFiles(
                     Path.Combine(FindRepoRoot(), "src", "WinClonerX.App", "Views"),
                     "*StepView.xaml",
                     SearchOption.TopDirectoryOnly))
        {
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            var relativePath = Path.GetRelativePath(FindRepoRoot(), file);

            foreach (var control in document.Descendants().Where(IsInteractiveControl))
            {
                if (!HasAccessibleName(control) && !HasLocalizedContent(control))
                {
                    failures.Add($"{relativePath}:{Line(control)} <{control.Name.LocalName}> has no accessible name or localized content.");
                }
            }

            foreach (var listBox in document.Descendants().Where(element => element.Name.LocalName == "ListBox"))
            {
                var itemTemplate = listBox.Elements().FirstOrDefault(element => element.Name.LocalName == "ListBox.ItemTemplate");
                if (itemTemplate is not null && !itemTemplate.Descendants().Any(HasAccessibleName))
                {
                    failures.Add($"{relativePath}:{Line(itemTemplate)} ListBox item template has no accessible name.");
                }
            }
        }

        failures.ShouldBeEmpty(string.Join(Environment.NewLine, failures));
    }

    private static bool IsInteractiveControl(XElement element) =>
        element.Name.LocalName is "Button" or "CheckBox" or "RadioButton" or "TextBox";

    private static bool HasAccessibleName(XElement element) =>
        element.Attributes().Any(attribute => attribute.Name.LocalName == "AutomationProperties.Name"
            && !string.IsNullOrWhiteSpace(attribute.Value))
        || element.Descendants().Any(descendant => descendant.Attributes().Any(attribute =>
            attribute.Name.LocalName == "AutomationProperties.Name" && !string.IsNullOrWhiteSpace(attribute.Value)));

    private static bool HasLocalizedContent(XElement element) =>
        element.Attributes().Any(attribute => (attribute.Name.LocalName is "Content" or "Header")
            && attribute.Value.StartsWith("{loc:Loc ", StringComparison.Ordinal));

    private static int Line(XElement element) => ((System.Xml.IXmlLineInfo)element).LineNumber;

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
