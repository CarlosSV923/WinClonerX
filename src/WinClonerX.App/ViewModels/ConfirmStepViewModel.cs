using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinClonerX.App.Localization;
using WinClonerX.App.Presentation;
using WinClonerX.Application.UseCases.Planning;
using WinClonerX.Domain.Inventory;
using WinClonerX.Domain.Planning;

namespace WinClonerX.App.ViewModels;

/// <summary>
/// Step 3 "Confirm" (contracts/ui-wizard.md): shows the disk that will be erased and asks for the last four characters of its
/// serial number (or of its GUID when it has none). For a plan that shrinks the source (FR-013), it also asks for a separate
/// authorization. "Clone now" is only enabled when the characters match and, if there is a shrink, it is authorized (FR-012, R13).
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

    /// <summary>Localized prompt with the full identifier shown on the disk card.</summary>
    [ObservableProperty]
    private string _promptText = string.Empty;

    /// <summary>Source of the identifier the person is asked to type.</summary>
    [ObservableProperty]
    private ConfirmationSource _identifierSource;

    /// <summary>True when the plan reduces the source disk (FR-013). Shows the shrink panel.</summary>
    [ObservableProperty]
    private bool _hasSourceShrink;

    /// <summary>Localized explanation of the temporary shrink: the sizes before and after and that it is restored.</summary>
    [ObservableProperty]
    private string _sourceShrinkText = string.Empty;

    /// <summary>The separate authorization of the source shrink, bound to its checkbox. Always false when there is no shrink.</summary>
    [ObservableProperty]
    private bool _confirmSourceShrink;

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
    /// Whether the step is complete: the characters match and, when the plan shrinks the source, the shrink is authorized.
    /// Only this drives the "Clone now" button and the validity of step 3.
    /// </summary>
    public bool IsReadyToClone => IsSuffixValid && (!HasSourceShrink || ConfirmSourceShrink);

    /// <summary>
    /// Confirms the plan. Enabled only when the characters match, the shrink is authorized if there is one, and the plan is known
    /// (FR-012, FR-013). The button is the default button of the step, so the keyboard can complete it with Enter.
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
            // Without a shrink in the plan the authorization is always false, even if the checkbox had been ticked before.
            await _confirmPlan.ExecuteAsync(
                plan.Id,
                _acceptedWarnings,
                TypedSuffix,
                HasSourceShrink && ConfirmSourceShrink,
                cancellationToken);
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
        IdentifierSource = TargetConfirmation.GetSource(target);
        var sourceKey = IdentifierSource switch
        {
            ConfirmationSource.SerialNumber => "Confirm_Source_Serial",
            ConfirmationSource.DiskGuid => "Confirm_Source_Guid",
            ConfirmationSource.Model => "Confirm_Source_Model",
            ConfirmationSource.DiskNumber => "Confirm_Source_DiskNumber",
            _ => throw new InvalidOperationException("Unknown confirmation source."),
        };
        PromptText = Loc.Format("Confirm_Prompt", Loc.Get(sourceKey), TargetConfirmation.ConfirmationIdentifier(target, IdentifierSource));

        HasSourceShrink = plan.SourceShrink is not null;
        SourceShrinkText = plan.SourceShrink is { } shrink
            ? Loc.Format(
                "Shrink_Confirm_Body",
                SizeText.Format(shrink.OriginalLength),
                SizeText.Format(shrink.ShrunkLength))
            : string.Empty;

        TypedSuffix = string.Empty;
        ConfirmSourceShrink = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsReadyToClone));
        CloneNowCommand.NotifyCanExecuteChanged();
    }

    private bool CanCloneNow() => IsReadyToClone && !IsBusy && _plan is not null;

    /// <summary>Clears the confirmation after its plan has been used.</summary>
    public void Clear()
    {
        _target = null;
        _plan = null;
        _acceptedWarnings = new HashSet<PreflightCode>();
        TargetCard = null;
        TypedSuffix = string.Empty;
        PromptText = string.Empty;
        IdentifierSource = default;
        HasSourceShrink = false;
        SourceShrinkText = string.Empty;
        ConfirmSourceShrink = false;
        ErrorMessage = null;
        OnPropertyChanged(nameof(IsSuffixValid));
        OnPropertyChanged(nameof(IsReadyToClone));
        CloneNowCommand.NotifyCanExecuteChanged();
    }

    partial void OnTypedSuffixChanged(string value)
    {
        OnPropertyChanged(nameof(IsSuffixValid));
        OnPropertyChanged(nameof(IsReadyToClone));
        CloneNowCommand.NotifyCanExecuteChanged();
    }

    partial void OnConfirmSourceShrinkChanged(bool value)
    {
        OnPropertyChanged(nameof(IsReadyToClone));
        CloneNowCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsBusyChanged(bool value)
    {
        CloneNowCommand.NotifyCanExecuteChanged();
    }

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
