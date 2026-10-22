using WinClonerX.App.Services;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>Info dialog fake that records each localized request without opening a window.</summary>
internal sealed class FakeInfoDialogService : IInfoDialogService
{
    public List<InfoDialogRequest> Requests { get; } = [];

    public void Show(InfoDialogRequest request) => Requests.Add(request);
}
