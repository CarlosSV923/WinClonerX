using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using WinClonerX.App.Services;

namespace WinClonerX.App.Views;

/// <summary>Informational dialog with one default and cancel button.</summary>
public partial class InfoDialog : Window
{
    public InfoDialog(InfoDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        InitializeComponent();
        Title = request.Title;
        SetText(TitleText, request.Title);
        SetText(IntroText, request.Intro);
        SetText(DetectsHeadingText, request.DetectsHeading);
        DetectsItems.ItemsSource = request.Detects;
        SetText(CostText, request.Cost);
        SetText(WhenHeadingText, request.WhenHeading);
        WhenItems.ItemsSource = request.When;
        SetText(NoteText, request.Note);
        CloseButton.Content = request.CloseText;
        AutomationProperties.SetName(CloseButton, request.CloseText);
        Loaded += (_, _) => CloseButton.Focus();
    }

    private static void SetText(TextBlock textBlock, string value)
    {
        textBlock.Text = value;
        AutomationProperties.SetName(textBlock, value);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();
}
