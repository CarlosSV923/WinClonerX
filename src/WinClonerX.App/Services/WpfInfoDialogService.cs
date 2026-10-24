using System.Windows;
using WinClonerX.App.Views;

namespace WinClonerX.App.Services;

/// <summary>WPF implementation: shows an informational dialog centered over the main window.</summary>
public sealed class WpfInfoDialogService : IInfoDialogService
{
    public void Show(InfoDialogRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var dialog = new InfoDialog(request);
        if (System.Windows.Application.Current?.MainWindow is { IsVisible: true } owner)
        {
            dialog.Owner = owner;
        }

        dialog.ShowDialog();
    }
}
