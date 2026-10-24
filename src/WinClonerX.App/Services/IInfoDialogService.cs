namespace WinClonerX.App.Services;

/// <summary>Shows informational content in a dialog, keeping view models testable without opening a window.</summary>
public interface IInfoDialogService
{
    /// <summary>Shows the supplied title and content until the person closes the dialog.</summary>
    void Show(InfoDialogRequest request);
}

/// <summary>Localized content for an informational dialog with two groups of bullet points.</summary>
public sealed record InfoDialogRequest(
    string Title,
    string Intro,
    string DetectsHeading,
    IReadOnlyList<string> Detects,
    string Cost,
    string WhenHeading,
    IReadOnlyList<string> When,
    string Note,
    string CloseText);
