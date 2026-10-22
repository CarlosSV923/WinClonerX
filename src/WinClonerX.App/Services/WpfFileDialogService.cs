using Microsoft.Win32;
using WinClonerX.App.Localization;

namespace WinClonerX.App.Services;

/// <summary>WPF implementation: the standard Save dialog, starting in the Documents folder and filtered to .zip files.</summary>
public sealed class WpfFileDialogService : IFileDialogService
{
    public string? PickExportFile(string suggestedFileName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(suggestedFileName);

        var dialog = new SaveFileDialog
        {
            FileName = suggestedFileName,
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Filter = Loc.Get("Result_Export_FileFilter"),
            DefaultExt = ".zip",
            AddExtension = true,
            OverwritePrompt = true,
        };

        var owner = System.Windows.Application.Current?.MainWindow;
        bool? accepted = owner is { IsVisible: true } ? dialog.ShowDialog(owner) : dialog.ShowDialog();

        return accepted == true ? dialog.FileName : null;
    }
}
