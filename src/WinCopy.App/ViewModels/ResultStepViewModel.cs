using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.App.Services;
using WinCopy.Application.Errors;
using WinCopy.Application.Ports;
using WinCopy.Application.UseCases;
using WinCopy.Domain.Execution;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Step 5 "Result" (contracts/ui-wizard.md). A successful clone shows the structural verification, the duration, the state of
/// WinRE and the next steps (FR-022, FR-023). A cancelled or failed one says that the target disk is unusable, the state of the
/// original disk (FR-024) and the failure details. Every outcome can export the log and open its folder (FR-026, FR-027).
/// </summary>
public sealed partial class ResultStepViewModel : ObservableObject
{
    /// <summary>Codes the use case produces that are not <see cref="PlatformErrorCode"/> values. Each has its own message.</summary>
    private static readonly HashSet<string> UseCaseFailureCodes = ["Unexpected", "DisksChanged", "ShrinkInsufficient", "ShrinkJournalFailed"];

    private readonly IExportLog _exportLog;
    private readonly IFileDialogService _fileDialog;
    private readonly IFolderOpener _folderOpener;
    private readonly IClock _clock;

    /// <summary>Creates the step with the services it needs to export the log and to open its folder.</summary>
    public ResultStepViewModel(IExportLog exportLog, IFileDialogService fileDialog, IFolderOpener folderOpener, IClock clock)
    {
        _exportLog = exportLog ?? throw new ArgumentNullException(nameof(exportLog));
        _fileDialog = fileDialog ?? throw new ArgumentNullException(nameof(fileDialog));
        _folderOpener = folderOpener ?? throw new ArgumentNullException(nameof(folderOpener));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    /// <summary>Outcome of the run. Null until <see cref="Show"/> is called.</summary>
    [ObservableProperty]
    private CloneOutcome? _outcome;

    /// <summary>Header of the result: success, cancelled or failed.</summary>
    [ObservableProperty]
    private string _title = string.Empty;

    /// <summary>Text under the header.</summary>
    [ObservableProperty]
    private string _body = string.Empty;

    /// <summary>Duration of the run, already formatted.</summary>
    [ObservableProperty]
    private string _durationLine = string.Empty;

    /// <summary>Whether a structural verification result exists.</summary>
    [ObservableProperty]
    private bool _hasVerification;

    /// <summary>One line with the outcome of the structural verification.</summary>
    [ObservableProperty]
    private string _verificationSummary = string.Empty;

    /// <summary>Issues found by the structural verification. Empty when it passed.</summary>
    public ObservableCollection<string> VerificationIssues { get; } = [];

    /// <summary>Whether the state of WinRE is known. Hidden otherwise.</summary>
    [ObservableProperty]
    private bool _hasRecovery;

    /// <summary>Localized state of WinRE: configured, or the warning that explains what to do.</summary>
    [ObservableProperty]
    private string _recoveryText = string.Empty;

    /// <summary>True when WinRE could not be configured. The text then carries the warning and the command to run.</summary>
    [ObservableProperty]
    private bool _isRecoveryWarning;

    /// <summary>Instructions for the person, in order.</summary>
    public ObservableCollection<string> NextSteps { get; } = [];

    /// <summary>Reason of a cancelled or failed run. Empty on success.</summary>
    [ObservableProperty]
    private string _reason = string.Empty;

    /// <summary>True when the report carries the failure of the run. <see cref="FailureDetail"/> then carries its code and phase.</summary>
    [ObservableProperty]
    private bool _hasFailureDetail;

    /// <summary>
    /// Localized technical line for support, for example "Code: MountFailed · Phase: Adjusting the clone so it starts". Shown small and
    /// selectable so it can be copied into a support request. Empty when there is no failure.
    /// </summary>
    [ObservableProperty]
    private string _failureDetail = string.Empty;

    /// <summary>True when the report names the log file of the run.</summary>
    [ObservableProperty]
    private bool _hasLogFile;

    /// <summary>Localized line with the path of the log file of the run. Empty when the report has none.</summary>
    [ObservableProperty]
    private string _logFileText = string.Empty;

    /// <summary>Path of the log file of the run, for every outcome. Empty when the report has none.</summary>
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLogFolderCommand))]
    private string _logFile = string.Empty;

    /// <summary>True when the source was shrunk and went back to its original size (FR-021a). Shown as a confirmation.</summary>
    [ObservableProperty]
    private bool _isSourceRestored;

    /// <summary>True when the source was shrunk and could not be restored. Shown as a prominent warning with the way to fix it.</summary>
    [ObservableProperty]
    private bool _isSourceRestoreWarning;

    /// <summary>Localized text of the restore state. Empty when the run did not shrink the source.</summary>
    [ObservableProperty]
    private string _sourceRestoreText = string.Empty;

    /// <summary>
    /// Whether the disk inventory goes into the exported archive. Off by default: it holds serial numbers, and nothing about the disks
    /// is exported without the person's choice (FR-027).
    /// </summary>
    [ObservableProperty]
    private bool _includeInventory;

    /// <summary>Localized outcome of the last export or of opening the folder. Empty until one of them happens.</summary>
    [ObservableProperty]
    private string _exportStatusText = string.Empty;

    /// <summary>True when <see cref="ExportStatusText"/> reports a failure. The view shows it in the danger colour.</summary>
    [ObservableProperty]
    private bool _isExportStatusError;

    public bool IsSuccess => Outcome == CloneOutcome.Completed;

    public bool IsFailure => Outcome is CloneOutcome.Failed or CloneOutcome.Cancelled;

    /// <summary>True when there is an export or folder message to show.</summary>
    public bool HasExportStatus => ExportStatusText.Length > 0;

    /// <summary>Exports the log of the run to a .zip the person picks (FR-026). Available for every outcome.</summary>
    [RelayCommand]
    private async Task ExportLogAsync()
    {
        string? destination = _fileDialog.PickExportFile(SuggestedExportFileName());
        if (destination is null)
        {
            // The person cancelled the dialog: nothing is exported and nothing is said.
            return;
        }

        try
        {
            await _exportLog.ExecuteAsync(destination, IncludeInventory, CancellationToken.None);
            ShowExportStatus(Loc.Format("Result_Export_Done", destination), isError: false);
        }
        catch (Exception ex)
        {
            ShowExportStatus(Loc.Format("Result_Export_Failed", ex.Message), isError: true);
        }
    }

    /// <summary>Opens the folder of the log in Explorer. The logs live in the hidden ProgramData folder.</summary>
    [RelayCommand(CanExecute = nameof(CanOpenLogFolder))]
    private void OpenLogFolder()
    {
        string? folder = Path.GetDirectoryName(LogFile);
        if (string.IsNullOrEmpty(folder))
        {
            return;
        }

        try
        {
            _folderOpener.Open(folder);
        }
        catch (Exception ex)
        {
            ShowExportStatus(Loc.Format("Result_OpenFolder_Failed", ex.Message), isError: true);
        }
    }

    private bool CanOpenLogFolder => !string.IsNullOrEmpty(Path.GetDirectoryName(LogFile));

    partial void OnOutcomeChanged(CloneOutcome? value)
    {
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsFailure));
    }

    partial void OnExportStatusTextChanged(string value)
    {
        OnPropertyChanged(nameof(HasExportStatus));
    }

    /// <summary>
    /// Shows the report of a run. The failure comes with the report, so the message does not depend on any event that may
    /// still be on its way to the UI. Replaces anything shown before.
    /// </summary>
    public void Show(CloneResultReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        Clear();
        Outcome = report.Outcome;
        LogFile = report.LogFile;
        DurationLine = Loc.Format("Result_Duration", DurationText.Format(report.Duration));

        if (report.Outcome == CloneOutcome.Completed)
        {
            ShowSuccess(report);
        }
        else
        {
            ShowFailure(report);
        }

        ShowSourceRestore(report.SourceRestored);
    }

    /// <summary>
    /// The restore state of a shrunk source: confirmed, or a warning with the manual fix. Null (no shrink) shows nothing.
    /// </summary>
    private void ShowSourceRestore(bool? sourceRestored)
    {
        switch (sourceRestored)
        {
            case true:
                IsSourceRestored = true;
                SourceRestoreText = Loc.Get("Result_SourceRestored");
                break;
            case false:
                IsSourceRestoreWarning = true;
                SourceRestoreText = Loc.Get("Result_SourceRestore_Failed");
                break;
        }
    }

    private void ShowSuccess(CloneResultReport report)
    {
        Title = Loc.Get("Result_Success_Title");
        Body = Loc.Get("Result_Success_Body");

        if (report.Structural is { } structural)
        {
            HasVerification = true;
            VerificationSummary = Loc.Get(structural.Passed ? "Result_Verification_Passed" : "Result_Verification_Failed");
            foreach (var issue in structural.Issues)
            {
                VerificationIssues.Add(issue);
            }
        }

        if (report.RecoveryEnvironment is { } recovery)
        {
            HasRecovery = true;
            IsRecoveryWarning = recovery == RecoveryEnvironmentState.Warning;
            RecoveryText = Loc.Get(IsRecoveryWarning ? "Result_Recovery_Warning" : "Result_Recovery_Configured");
        }

        foreach (var step in report.NextSteps)
        {
            NextSteps.Add(Loc.Get("NextStep_" + step));
        }
    }

    private void ShowFailure(CloneResultReport report)
    {
        var outcome = report.Outcome;
        Title = Loc.Get(outcome == CloneOutcome.Cancelled ? "Result_Cancelled_Title" : "Result_Failed_Title");
        Body = outcome == CloneOutcome.Cancelled ? CancelledBody(report) : Loc.Get("Result_Failed_Body");
        Reason = outcome == CloneOutcome.Cancelled
            ? Loc.Get("Error_" + PlatformErrorCode.Cancelled)
            : ReasonFor(report.Failure?.Code);

        if (report.Failure is { } failure)
        {
            HasFailureDetail = true;
            FailureDetail = Loc.Format("Result_Failure_Detail", failure.Code, Loc.Get("Phase_" + failure.Phase));
        }

        if (!string.IsNullOrWhiteSpace(report.LogFile))
        {
            HasLogFile = true;
            LogFileText = Loc.Format("Result_LogFile", report.LogFile);
        }

        NextSteps.Add(Loc.Get("Result_Failed_KeepOriginal"));
    }

    /// <summary>
    /// Text of a cancelled run: the target disk was left uninitialized. The original disk is only said to be untouched when the
    /// run did not shrink it; a shrunk one is covered by the restore line (<see cref="ShowSourceRestore"/>).
    /// </summary>
    private static string CancelledBody(CloneResultReport report) => report.SourceRestored is null
        ? Loc.Get("Result_Cancelled_Body") + " " + Loc.Get("Result_Cancelled_SourceUntouched")
        : Loc.Get("Result_Cancelled_Body");

    /// <summary>
    /// The message for this failure code: the platform error or the use case message with that code, or the generic message
    /// for codes this step does not know.
    /// </summary>
    private static string ReasonFor(string? failureCode)
    {
        if (failureCode is null)
        {
            return Loc.Get("Result_Failed_Generic");
        }

        if (UseCaseFailureCodes.Contains(failureCode))
        {
            return Loc.Get("Error_" + failureCode);
        }

        return Enum.TryParse<PlatformErrorCode>(failureCode, ignoreCase: false, out var code) && Enum.IsDefined(code)
            ? Loc.Get("Error_" + code)
            : Loc.Get("Result_Failed_Generic");
    }

    /// <summary>Default name of the exported archive, from the local time: WinCopy-registro-yyyyMMdd-HHmmss.zip.</summary>
    private string SuggestedExportFileName() =>
        "WinCopy-registro-" + _clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".zip";

    private void ShowExportStatus(string text, bool isError)
    {
        ExportStatusText = text;
        IsExportStatusError = isError;
    }

    private void Clear()
    {
        Outcome = null;
        Title = string.Empty;
        Body = string.Empty;
        DurationLine = string.Empty;
        HasVerification = false;
        VerificationSummary = string.Empty;
        VerificationIssues.Clear();
        HasRecovery = false;
        IsRecoveryWarning = false;
        RecoveryText = string.Empty;
        NextSteps.Clear();
        Reason = string.Empty;
        HasFailureDetail = false;
        FailureDetail = string.Empty;
        HasLogFile = false;
        LogFileText = string.Empty;
        LogFile = string.Empty;
        IsSourceRestored = false;
        IsSourceRestoreWarning = false;
        SourceRestoreText = string.Empty;
        ExportStatusText = string.Empty;
        IsExportStatusError = false;
    }
}
