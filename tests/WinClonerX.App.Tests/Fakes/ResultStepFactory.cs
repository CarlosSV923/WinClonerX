using WinClonerX.App.ViewModels;

namespace WinClonerX.App.Tests.Fakes;

/// <summary>Builds a <see cref="ResultStepViewModel"/> with fakes for the export and folder services that nothing in the test uses.</summary>
internal static class ResultStepFactory
{
    public static ResultStepViewModel Create() => new(
        new FakeExportLog(),
        new FakeFileDialogService(),
        new FakeFolderOpener(),
        new FixedClock(),
        new FakeAppLifetime());
}
