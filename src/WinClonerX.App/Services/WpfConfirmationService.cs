using System.Windows;
using WinClonerX.App.Views;

namespace WinClonerX.App.Services;

/// <summary>
/// WPF implementation: shows <see cref="ConfirmationDialog"/> over the main window. A dialog with custom button texts is used
/// because the standard message box cannot say "Cancel cloning" and "Keep cloning".
/// </summary>
public sealed class WpfConfirmationService : IConfirmationService
{
    public bool Confirm(ConfirmationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new ConfirmationDialog(request);
        if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true;
    }
}
