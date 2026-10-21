namespace WinCopy.App.Services;

/// <summary>
/// Opens a folder in the file manager. View models depend on this abstraction, so tests can record the folder without opening
/// a window.
/// </summary>
public interface IFolderOpener
{
    /// <summary>Opens <paramref name="folderPath"/> in Windows Explorer.</summary>
    void Open(string folderPath);
}
