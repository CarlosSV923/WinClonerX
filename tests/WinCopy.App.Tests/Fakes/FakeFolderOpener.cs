using WinCopy.App.Services;

namespace WinCopy.App.Tests.Fakes;

/// <summary>Folder opener fake: records the folders it was asked to open, or throws <see cref="Failure"/> instead.</summary>
internal sealed class FakeFolderOpener : IFolderOpener
{
    /// <summary>Folders asked to open, in order.</summary>
    public List<string> OpenedFolders { get; } = [];

    /// <summary>Exception thrown by the next open. Null, the default, means the folder opens.</summary>
    public Exception? Failure { get; set; }

    public void Open(string folderPath)
    {
        if (Failure is not null)
        {
            throw Failure;
        }

        OpenedFolders.Add(folderPath);
    }
}
