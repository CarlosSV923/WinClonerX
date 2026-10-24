namespace WinClonerX.App.Services;

/// <summary>
/// Asks the person where to save a file. View models depend on this abstraction, so tests can answer in advance without
/// showing a window.
/// </summary>
public interface IFileDialogService
{
    /// <summary>
    /// Shows the Save dialog for a .zip export, with <paramref name="suggestedFileName"/> as the name. Returns the full path
    /// the person chose, or null if they cancelled.
    /// </summary>
    string? PickExportFile(string suggestedFileName);
}
