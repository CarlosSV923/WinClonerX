using WinClonerX.App.Services;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>Save dialog fake: answers with <see cref="Answer"/> and records the suggested names it was given.</summary>
internal sealed class FakeFileDialogService : IFileDialogService
{
    /// <summary>Path the person picks. Null, the default, means the person cancelled the dialog.</summary>
    public string? Answer { get; set; }

    /// <summary>Suggested file names, in the order the dialog was shown.</summary>
    public List<string> SuggestedNames { get; } = [];

    public string? PickExportFile(string suggestedFileName)
    {
        SuggestedNames.Add(suggestedFileName);
        return Answer;
    }
}
