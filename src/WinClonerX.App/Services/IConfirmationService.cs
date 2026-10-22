namespace WinClonerX.App.Services;

/// <summary>
/// Asks the person to confirm an action. View models depend on this abstraction, so they can be tested without showing a
/// window; tests replace it with a fake that answers in advance.
/// </summary>
public interface IConfirmationService
{
    /// <summary>Shows the question. Returns true only when the person picks the confirming option.</summary>
    bool Confirm(ConfirmationRequest request);
}

/// <summary>
/// A question with two options: <see cref="ConfirmText"/> confirms (true) and <see cref="DeclineText"/> declines (false).
/// </summary>
public sealed record ConfirmationRequest(string Title, string Message, string ConfirmText, string DeclineText);
