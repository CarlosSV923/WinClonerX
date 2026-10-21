using WinCopy.App.Services;

namespace WinCopy.App.Tests.Fakes;

/// <summary>Confirmation fake: answers with <see cref="Answer"/> and records every question it was asked.</summary>
internal sealed class FakeConfirmationService : IConfirmationService
{
    /// <summary>What the person answers. False by default, the safe answer.</summary>
    public bool Answer { get; set; }

    /// <summary>Questions asked, in order.</summary>
    public List<ConfirmationRequest> Questions { get; } = [];

    public bool Confirm(ConfirmationRequest request)
    {
        Questions.Add(request);
        return Answer;
    }
}
