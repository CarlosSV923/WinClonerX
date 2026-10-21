using System.Windows;
using System.Windows.Automation;
using WinCopy.App.Services;

namespace WinCopy.App.Views;

/// <summary>Dialog with two options, used to confirm a destructive or irreversible action (for example, cancelling a clone).</summary>
public partial class ConfirmationDialog : Window
{
    public ConfirmationDialog(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        InitializeComponent();
        Title = request.Title;
        MessageText.Text = request.Message;
        AutomationProperties.SetName(MessageText, request.Message);
        ConfirmButton.Content = request.ConfirmText;
        AutomationProperties.SetName(ConfirmButton, request.ConfirmText);
        DeclineButton.Content = request.DeclineText;
        AutomationProperties.SetName(DeclineButton, request.DeclineText);
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e) => DialogResult = true;

    private void OnDeclineClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
