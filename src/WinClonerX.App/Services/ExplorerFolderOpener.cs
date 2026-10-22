using System.Diagnostics;

namespace WinClonerX.App.Services;

/// <summary>
/// Opens a folder with explorer.exe. The logs live in the hidden <c>%ProgramData%</c> folder, so the person cannot find them by
/// hand. The path is passed as a separate argument, so spaces in it are safe.
/// </summary>
public sealed class ExplorerFolderOpener : IFolderOpener
{
    public void Open(string folderPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folderPath);

        var start = new ProcessStartInfo("explorer.exe") { UseShellExecute = false };
        start.ArgumentList.Add(folderPath);

        using var process = Process.Start(start);
    }
}
