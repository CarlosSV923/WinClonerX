using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WinCopy.Domain.Execution;
using WinCopy.Domain.Planning;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Navigation shell of the cloning wizard: the five steps, the current position and the Back/Next rules.
/// Steps with a page view model drive <see cref="IsCurrentStepValid"/> from that page. Step 3 (Confirm) advances through its own
/// "Clone now" action and step 4 (Progress) advances by itself when the run ends, so the generic Next is hidden on both.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    private const int ConfirmIndex = (int)WizardStepKind.Confirm;
    private const int ProgressIndex = (int)WizardStepKind.Progress;
    private const int ResultIndex = (int)WizardStepKind.Result;

    private readonly ConfirmStepViewModel? _confirm;
    private readonly ProgressStepViewModel? _progress;
    private readonly ResultStepViewModel? _result;

    private int _currentIndex;
    private bool _isCurrentStepValid = true;

    /// <summary>Creates the shell without step pages. Each step is valid until a page says otherwise.</summary>
    public WizardViewModel()
    {
        Steps =
        [
            new WizardStep(WizardStepKind.Disks, "Wizard_Step_Disks"),
            new WizardStep(WizardStepKind.Review, "Wizard_Step_Review"),
            new WizardStep(WizardStepKind.Confirm, "Wizard_Step_Confirm"),
            new WizardStep(WizardStepKind.Progress, "Wizard_Step_Progress"),
            new WizardStep(WizardStepKind.Result, "Wizard_Step_Result"),
        ];

        StepItems = Steps
            .Select((step, index) => new WizardStepItemViewModel(step, index + 1))
            .ToArray();

        UpdateStepItems();
    }

    /// <summary>Creates the shell with the Disks and Review pages. Their validity drives Next.</summary>
    public WizardViewModel(DisksStepViewModel disksStep, ReviewStepViewModel reviewStep)
        : this()
    {
        ArgumentNullException.ThrowIfNull(disksStep);
        ArgumentNullException.ThrowIfNull(reviewStep);

        DisksStep = disksStep;
        ReviewStep = reviewStep;
        disksStep.PropertyChanged += OnStepPagePropertyChanged;
        reviewStep.PropertyChanged += OnStepPagePropertyChanged;
        RefreshStepValidity();
    }

    /// <summary>Creates the shell with all five pages. The confirmation starts the run, and the run ends on the result page.</summary>
    public WizardViewModel(
        DisksStepViewModel disksStep,
        ReviewStepViewModel reviewStep,
        ConfirmStepViewModel confirmStep,
        ProgressStepViewModel progressStep,
        ResultStepViewModel resultStep)
        : this(disksStep, reviewStep)
    {
        ArgumentNullException.ThrowIfNull(confirmStep);
        ArgumentNullException.ThrowIfNull(progressStep);
        ArgumentNullException.ThrowIfNull(resultStep);

        _confirm = confirmStep;
        _progress = progressStep;
        _result = resultStep;

        confirmStep.PropertyChanged += OnStepPagePropertyChanged;
        confirmStep.PlanConfirmed += OnPlanConfirmed;
        confirmStep.ReturnToDisksRequested += OnReturnToDisksRequested;
        RefreshStepValidity();
    }

    public IReadOnlyList<WizardStep> Steps { get; }

    /// <summary>One item per step, for the step rail. Current and completed flags follow <see cref="CurrentIndex"/>.</summary>
    public IReadOnlyList<WizardStepItemViewModel> StepItems { get; }

    /// <summary>Page of step 1, or null when the shell was created without pages.</summary>
    public DisksStepViewModel? DisksStep { get; }

    /// <summary>Page of step 2, or null when the shell was created without pages.</summary>
    public ReviewStepViewModel? ReviewStep { get; }

    /// <summary>Page of step 3, or null when the shell was created without it.</summary>
    public ConfirmStepViewModel? ConfirmStep => _confirm;

    /// <summary>Page of step 4, or null when the shell was created without it.</summary>
    public ProgressStepViewModel? ProgressStep => _progress;

    /// <summary>Page of step 5, or null when the shell was created without it.</summary>
    public ResultStepViewModel? ResultStep => _result;

    /// <summary>
    /// Message shown above the page, for example after the disks changed and the wizard went back to step 1.
    /// Cleared when the person moves with Back or Next.
    /// </summary>
    [ObservableProperty]
    private string? _notice;

    /// <summary>Completes when the run has finished and the result page is shown. Lets callers and tests wait for it.</summary>
    public Task ExecutionCompletion { get; private set; } = Task.CompletedTask;

    public int CurrentIndex
    {
        get => _currentIndex;
        private set
        {
            if (!SetProperty(ref _currentIndex, value))
            {
                return;
            }

            OnPropertyChanged(nameof(CurrentStep));
            OnPropertyChanged(nameof(CurrentPage));
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoNext));
            OnPropertyChanged(nameof(IsNextVisible));
            BackCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            RefreshStepValidity();
            UpdateStepItems();
        }
    }

    public WizardStep CurrentStep => Steps[CurrentIndex];

    /// <summary>
    /// What the content area shows: the page view model of the current step, or the step itself when it has no page yet.
    /// </summary>
    public object CurrentPage => CurrentStep.Kind switch
    {
        WizardStepKind.Disks when DisksStep is not null => DisksStep,
        WizardStepKind.Review when ReviewStep is not null => ReviewStep,
        WizardStepKind.Confirm when _confirm is not null => _confirm,
        WizardStepKind.Progress when _progress is not null => _progress,
        WizardStepKind.Result when _result is not null => _result,
        _ => CurrentStep,
    };

    /// <summary>Whether the current step's input is valid. Next is only enabled when it is true.</summary>
    public bool IsCurrentStepValid
    {
        get => _isCurrentStepValid;
        set
        {
            if (SetProperty(ref _isCurrentStepValid, value))
            {
                OnPropertyChanged(nameof(CanGoNext));
                NextCommand.NotifyCanExecuteChanged();
            }
        }
    }

    /// <summary>True when a notice is shown above the page.</summary>
    public bool HasNotice => !string.IsNullOrEmpty(Notice);

    public bool CanGoBack =>
        CurrentIndex > 0 && CurrentStep.Kind is not (WizardStepKind.Progress or WizardStepKind.Result);

    /// <summary>
    /// Whether the generic Next button applies. Step 3 has its own "Clone now" action, and step 4 advances by itself.
    /// </summary>
    public bool IsNextVisible => CurrentStep.Kind is not (WizardStepKind.Confirm or WizardStepKind.Progress);

    public bool CanGoNext => CurrentIndex < Steps.Count - 1 && IsCurrentStepValid && IsNextVisible;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        // With the pages attached, steps 3 and 4 advance only through their own actions.
        if (CurrentIndex == ConfirmIndex && _confirm is not null || CurrentIndex == ProgressIndex && _progress is not null)
        {
            return;
        }

        Notice = null;

        var nextKind = Steps[CurrentIndex + 1].Kind;
        var enteringReview = nextKind == WizardStepKind.Review;
        if (enteringReview)
        {
            // Forget any plan from an earlier visit before the index changes, so Next stays disabled until the new plan exists.
            ReviewStep?.Clear();
        }

        if (nextKind == WizardStepKind.Confirm)
        {
            PrepareConfirmation();
        }

        CurrentIndex++;

        if (enteringReview && ReviewStep is not null && DisksStep?.SelectedTarget is { } target)
        {
            await ReviewStep.LoadAsync(target.DiskId);
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        Notice = null;
        CurrentIndex--;
    }

    /// <summary>Gives step 3 the disk to erase, the plan and the accepted warnings. Called before the step is shown.</summary>
    private void PrepareConfirmation()
    {
        if (_confirm is not null && DisksStep?.SelectedTarget is { } target && ReviewStep?.Plan is { } plan)
        {
            _confirm.Prepare(target.Disk, plan, ReviewStep.AcceptedWarnings);
        }
    }

    /// <summary>The plan is confirmed: move to the progress page and run it. The result page follows the run.</summary>
    private void OnPlanConfirmed(object? sender, PlanConfirmedEventArgs e)
    {
        CurrentIndex = ProgressIndex;
        ExecutionCompletion = RunExecutionAsync(e.PlanId);
    }

    private async Task RunExecutionAsync(PlanId planId)
    {
        if (_progress is null || _result is null)
        {
            return;
        }

        CloneResultReport report;
        try
        {
            report = await _progress.StartAsync(planId);
        }
        catch (InvalidOperationException)
        {
            // The use case refuses to run a plan that is not confirmed. Nothing was touched, so the result says it failed.
            report = new CloneResultReport(
                CloneOutcome.Failed,
                null,
                null,
                null,
                null,
                TimeSpan.Zero,
                string.Empty,
                [NextStepCode.KeepOriginalDiskUntilVerified]);
        }

        _result.Show(report, _progress.FailureCode);
        CurrentIndex = ResultIndex;
    }

    /// <summary>The disks changed after the plan was made: back to step 1, which reads the disks again.</summary>
    private void OnReturnToDisksRequested(object? sender, ReturnToDisksEventArgs e)
    {
        ReviewStep?.Clear();
        CurrentIndex = 0;
        Notice = e.Message;
        DisksStep?.RefreshCommand.Execute(null);
    }

    partial void OnNoticeChanged(string? value) => OnPropertyChanged(nameof(HasNotice));

    private void OnStepPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(DisksStepViewModel.IsValid) or nameof(ReviewStepViewModel.IsValid)
            or nameof(ConfirmStepViewModel.IsSuffixValid))
        {
            RefreshStepValidity();
        }
    }

    private void RefreshStepValidity()
    {
        switch (CurrentStep.Kind)
        {
            case WizardStepKind.Disks when DisksStep is not null:
                IsCurrentStepValid = DisksStep.IsValid;
                break;
            case WizardStepKind.Review when ReviewStep is not null:
                IsCurrentStepValid = ReviewStep.IsValid;
                break;
            case WizardStepKind.Confirm when _confirm is not null:
                IsCurrentStepValid = _confirm.IsSuffixValid;
                break;
        }
    }

    private void UpdateStepItems()
    {
        for (var index = 0; index < StepItems.Count; index++)
        {
            StepItems[index].IsCurrent = index == CurrentIndex;
            StepItems[index].IsCompleted = index < CurrentIndex;
        }
    }
}
