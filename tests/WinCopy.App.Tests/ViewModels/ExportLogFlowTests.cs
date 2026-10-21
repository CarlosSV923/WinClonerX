using System.Globalization;
using System.IO;
using Shouldly;
using WinCopy.App.Localization;
using WinCopy.App.Tests.Fakes;
using WinCopy.App.ViewModels;
using WinCopy.Domain.Execution;
using Xunit;

namespace WinCopy.App.Tests.ViewModels;

/// <summary>Exportar el registro y abrir su carpeta desde el paso Resultado (T104, T105, FR-026, FR-027).</summary>
public sealed class ExportLogFlowTests
{
    private const string LogFile = @"C:\ProgramData\WinCopy\logs\winCopy-20261020-101530-0123456789abcdef0123456789abcdef.clef";
    private const string Destination = @"C:\Users\Test\Documents\WinCopy-registro-20261020-101530.zip";

    [Fact]
    public async Task ExportWithoutTheInventoryIsTheDefault()
    {
        using var _ = new EnglishCulture();
        var (vm, export, dialog, _) = Create();
        dialog.Answer = Destination;

        await vm.ExportLogCommand.ExecuteAsync(null);

        vm.IncludeInventory.ShouldBeFalse();
        export.Exports.ShouldBe([(Destination, false)]);
    }

    [Fact]
    public async Task ExportWithTheInventoryPassesTheFlag()
    {
        using var _ = new EnglishCulture();
        var (vm, export, dialog, _) = Create();
        dialog.Answer = Destination;
        vm.IncludeInventory = true;

        await vm.ExportLogCommand.ExecuteAsync(null);

        export.Exports.ShouldBe([(Destination, true)]);
    }

    [Fact]
    public async Task TheDialogSuggestsTheDatedNameInLocalTime()
    {
        using var _ = new EnglishCulture();
        var (vm, _, dialog, _) = Create();

        await vm.ExportLogCommand.ExecuteAsync(null);

        string stamp = new DateTimeOffset(2026, 10, 20, 10, 15, 30, TimeSpan.Zero)
            .ToLocalTime()
            .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        dialog.SuggestedNames.ShouldBe(["WinCopy-registro-" + stamp + ".zip"]);
    }

    [Fact]
    public async Task CancelledDialogExportsNothingAndSaysNothing()
    {
        using var _ = new EnglishCulture();
        var (vm, export, dialog, _) = Create();
        dialog.Answer = null;

        await vm.ExportLogCommand.ExecuteAsync(null);

        export.Exports.ShouldBeEmpty();
        vm.HasExportStatus.ShouldBeFalse();
        vm.ExportStatusText.ShouldBeEmpty();
    }

    [Fact]
    public async Task SuccessfulExportShowsTheDestinationInAPositiveLine()
    {
        using var _ = new EnglishCulture();
        var (vm, _, dialog, _) = Create();
        dialog.Answer = Destination;

        await vm.ExportLogCommand.ExecuteAsync(null);

        vm.ExportStatusText.ShouldBe(Loc.Format("Result_Export_Done", Destination));
        vm.IsExportStatusError.ShouldBeFalse();
        vm.HasExportStatus.ShouldBeTrue();
    }

    [Fact]
    public async Task ExportErrorShowsAnErrorLineAndDoesNotThrow()
    {
        using var _ = new EnglishCulture();
        var (vm, export, dialog, _) = Create();
        dialog.Answer = Destination;
        export.Failure = new IOException("The disk is full.");

        await vm.ExportLogCommand.ExecuteAsync(null);

        vm.ExportStatusText.ShouldBe(Loc.Format("Result_Export_Failed", "The disk is full."));
        vm.IsExportStatusError.ShouldBeTrue();
        vm.HasExportStatus.ShouldBeTrue();
    }

    [Fact]
    public void OpenLogFolderOpensTheFolderOfTheReportsLogFile()
    {
        using var _ = new EnglishCulture();
        var (vm, _, _, folder) = Create();

        vm.OpenLogFolderCommand.Execute(null);

        folder.OpenedFolders.ShouldBe([Path.GetDirectoryName(LogFile)!]);
        vm.HasExportStatus.ShouldBeFalse();
    }

    [Fact]
    public void OpenLogFolderFailureShowsAnErrorLine()
    {
        using var _ = new EnglishCulture();
        var (vm, _, _, folder) = Create();
        folder.Failure = new InvalidOperationException("explorer.exe is missing");

        vm.OpenLogFolderCommand.Execute(null);

        vm.ExportStatusText.ShouldBe(Loc.Format("Result_OpenFolder_Failed", "explorer.exe is missing"));
        vm.IsExportStatusError.ShouldBeTrue();
    }

    [Fact]
    public void OpenLogFolderIsNotAvailableWithoutALogFile()
    {
        using var _ = new EnglishCulture();
        var (vm, _, _, _) = Create();

        vm.Show(Report(CloneOutcome.Failed, logFile: string.Empty));

        vm.OpenLogFolderCommand.CanExecute(null).ShouldBeFalse();
        vm.ExportLogCommand.CanExecute(null).ShouldBeTrue();
    }

    [Theory]
    [InlineData(CloneOutcome.Completed)]
    [InlineData(CloneOutcome.Failed)]
    [InlineData(CloneOutcome.Cancelled)]
    public void BothButtonsAreAvailableForEveryOutcome(CloneOutcome outcome)
    {
        using var _ = new EnglishCulture();
        var (vm, _, _, _) = Create();

        vm.Show(Report(outcome));

        vm.ExportLogCommand.CanExecute(null).ShouldBeTrue();
        vm.OpenLogFolderCommand.CanExecute(null).ShouldBeTrue();
    }

    [Fact]
    public async Task ShowingAnotherReportClearsTheExportMessage()
    {
        using var _ = new EnglishCulture();
        var (vm, export, dialog, _) = Create();
        dialog.Answer = Destination;
        export.Failure = new IOException("The disk is full.");
        await vm.ExportLogCommand.ExecuteAsync(null);

        vm.Show(Report(CloneOutcome.Cancelled));

        vm.HasExportStatus.ShouldBeFalse();
        vm.IsExportStatusError.ShouldBeFalse();
    }

    private static (ResultStepViewModel Vm, FakeExportLog Export, FakeFileDialogService Dialog, FakeFolderOpener Folder) Create()
    {
        var export = new FakeExportLog();
        var dialog = new FakeFileDialogService();
        var folder = new FakeFolderOpener();
        var vm = new ResultStepViewModel(export, dialog, folder, new FixedClock());
        vm.Show(Report(CloneOutcome.Completed));

        return (vm, export, dialog, folder);
    }

    private static CloneResultReport Report(CloneOutcome outcome, string logFile = LogFile) => new(
        outcome,
        outcome == CloneOutcome.Completed ? new VerificationResult(true, []) : null,
        null,
        null,
        outcome == CloneOutcome.Completed ? RecoveryEnvironmentState.Configured : null,
        TimeSpan.FromMinutes(5),
        logFile,
        [NextStepCode.KeepOriginalDiskUntilVerified],
        outcome == CloneOutcome.Failed ? new ExecutionFailure("MountFailed", ExecutionPhase.RepairBoot, "technical detail") : null);
}
