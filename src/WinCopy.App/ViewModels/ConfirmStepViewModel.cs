using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCopy.App.Localization;
using WinCopy.Application.UseCases.Planning;
using WinCopy.Domain.Inventory;
using WinCopy.Domain.Planning;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Step 3 "Confirm" (contracts/ui-wizard.md): shows the disk that will be erased and asks for the last four characters of its
/// serial number (or of its GUID when it has none). "Clone now" is only enabled when they match (FR-012, R13).
/// The shrink confirmation of the source is US2 and is not implemented here.
/// </summary>
public sealed partial class ConfirmStepViewModel : ObservableObject
{
    private readonly IConfirmClonePlan _confirmPlan;
    private Disk? _target;
    private ClonePlan? _plan;
    private IReadOnlySet<PreflightCode> _acceptedWarnings = new HashSet<PreflightCode>();

    public ConfirmStepViewModel(IConfirmClonePlan confirmPlan)
    {
        _confirmPlan = confirmPlan ?? throw new ArgumentNullException(nameof(confirmPlan));
    }

    /// <summary>The disk that will be erased, drawn as on the disks step: model, full serial number, capacity and partitions.</summary>
    [ObservableProperty]
    private DiskCardViewModel? _targetCard;

    /// <summary>What the person typed to identify the disk.</summary>
    [ObservableProperty]
    private string _typedSuffix = string.Empty;

    /// <summary>Localized prompt. It shows the length of the expected characters, never their value.</summary>
    [ObservableProperty]
    private string _promptText = string.Empty;

    /// <summary>True when the disk reports no serial number, so the GUID is asked for instead.</summary>
    [ObservableProperty]
    private bool _usesGuidFallback;

    /// <summary>True while the confirmation is being saved.</summary>
    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Localized error when the confirmation could not be saved. Empty when there is none.</summary>
    [ObservableProperty]
    private string? _errorMessage;

    /// <summary>
    /// Whether the typed characters match the disk (case-insensitive). This is the validity of the step: the generic Next
    /// button is not used on this step.
    /// </summary>
    public bool IsSuffixValid => _target is not null
        && string.Equals(TypedSuffix.Trim(), TargetConfirmation.ExpectedSuffix(_target), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Confirms the plan. Enabled only when the characters match and the plan is known (FR-012). The button is the default
    /// button of the step, so the keyboard can complete it with Enter.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCloneNow))]
    private async Task CloneNowAsync(CancellationToken cancellationToken)
    {
        if (_plan is not { } plan)
        {
            return;
        }

        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _confirmPlan.ExecuteAsync(plan.Id, _acceptedWarnings, TypedSuffix, cancellationToken);
            PlanConfirmed?.Invoke(this, new PlanConfirmedEventArgs(plan.Id));
        }
        catch (PlanInvalidatedException)
        {
            // The disks changed since the plan was made: the person has to review them again (FR-011).
            TypedSuffix = string.Empty;
            ReturnToDisksRequested?.Invoke(this, new ReturnToDisksEventArgs(Loc.Get("Confirm_PlanInvalidated")));
        }
        catch (InvalidOperationException)
        {
            ErrorMessage = Loc.Get("Confirm_Failed");
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Raised after the plan is confirmed. The wizard then starts the clone.</summary>
    public event EventHandler<PlanConfirmedEventArgs>? PlanConfirmed;

    /// <summary>Raised when the disks changed after the plan was made. The wizard goes back to step 1.</summary>
    public event EventHandler<ReturnToDisksEventArgs>? ReturnToDisksRequested;

    /// <summary>
    /// Sets the disk, the plan and the accepted warnings for this confirmation, and clears what was typed before.
    /// Called when the wizard enters the step.
    /// </summary>
    public void Prepare(Disk target, ClonePlan plan, IReadOnlySet<PreflightCode> acceptedWarnings)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(acceptedWarnings);

        _target = target;
        _plan = plan;
        _acceptedWarnings = acceptedWarnings;

        TargetCard = new DiskCardViewModel(target);
        UsesGuidFallback = target.SerialNumber.Trim().Length == 0;
        PromptText = UsesGuidFallback
            ? Loc.Format("Confirm_Prompt_Guid", ExpectedMask())
            : Loc.Format("Confirm_Prompt_Serial", ExpectedMask());

        TypedSuffix = string.Empty;
        ErrorMessage = null;
        CloneNowCommand.NotifyCanExecuteChanged();
    }

    private bool CanCloneNow() => IsSuffixValid && !IsBusy && _plan is not null;

    partial void OnTypedSuffixChanged(string value)
    {
        OnPropertyChanged(nameof(IsSuffixValid));
        CloneNowCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        CloneNowCommand.NotifyCanExecuteChanged();
    }

    /// <summary>The expected characters replaced by X, so the prompt shows the length without the value.</summary>
    private string ExpectedMask() => _target is null
        ? string.Empty
        : new string('X', TargetConfirmation.ExpectedSuffix(_target).Length);
}

/// <summary>Arguments of <see cref="ConfirmStepViewModel.PlanConfirmed"/>.</summary>
public sealed class PlanConfirmedEventArgs(PlanId planId) : EventArgs
{
    public PlanId PlanId { get; } = planId;
}

/// <summary>Arguments of <see cref="ConfirmStepViewModel.ReturnToDisksRequested"/>: the message to show on step 1.</summary>
public sealed class ReturnToDisksEventArgs(string message) : EventArgs
{
    public string Message { get; } = message;
}
