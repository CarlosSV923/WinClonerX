using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WinCopy.App.Localization;
using WinCopy.App.Presentation;
using WinCopy.Application.Errors;
using WinCopy.Domain.Execution;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Step 5 "Result" (contracts/ui-wizard.md). A successful clone shows the structural verification, the duration, the state of
/// WinRE and the next steps (FR-022, FR-023). A cancelled or failed one shows a minimal explanation for now.
/// TODO(US3): the full variants of cancelled and failed results (state of both disks, log export).
/// </summary>
public sealed partial class ResultStepViewModel : ObservableObject
{
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

    public bool IsSuccess => Outcome == CloneOutcome.Completed;

    public bool IsFailure => Outcome is CloneOutcome.Failed or CloneOutcome.Cancelled;

    /// <summary>
    /// Log export is shown disabled until US3 adds the save dialog (FR-026).
    /// TODO(US3): enable "Export log".
    /// </summary>
    public bool IsExportLogAvailable { get; }

    partial void OnOutcomeChanged(CloneOutcome? value)
    {
        OnPropertyChanged(nameof(IsSuccess));
        OnPropertyChanged(nameof(IsFailure));
    }

    /// <summary>
    /// Shows the report of a run. <paramref name="failureCode"/> is the code of the failure, when there was one; it selects the
    /// message for the reason of a failed run. Replaces anything shown before.
    /// </summary>
    public void Show(CloneResultReport report, string? failureCode = null)
    {
        ArgumentNullException.ThrowIfNull(report);

        Clear();
        Outcome = report.Outcome;
        DurationLine = Loc.Format("Result_Duration", DurationText.Format(report.Duration));

        if (report.Outcome == CloneOutcome.Completed)
        {
            ShowSuccess(report);
        }
        else
        {
            ShowFailure(report.Outcome, failureCode);
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

    private void ShowFailure(CloneOutcome outcome, string? failureCode)
    {
        Title = Loc.Get(outcome == CloneOutcome.Cancelled ? "Result_Cancelled_Title" : "Result_Failed_Title");
        Body = Loc.Get("Result_Failed_Body");
        Reason = outcome == CloneOutcome.Cancelled
            ? Loc.Get("Error_" + PlatformErrorCode.Cancelled)
            : ReasonFor(failureCode);

        NextSteps.Add(Loc.Get("Result_Failed_KeepOriginal"));
    }

    /// <summary>The message of the platform error with this code, or the generic message for unknown codes.</summary>
    private static string ReasonFor(string? failureCode) =>
        failureCode is not null && Enum.TryParse<PlatformErrorCode>(failureCode, ignoreCase: false, out var code)
            && Enum.IsDefined(code)
            ? Loc.Get("Error_" + code)
            : Loc.Get("Result_Failed_Generic");

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
    }
}
