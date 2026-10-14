using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Navigation shell of the cloning wizard: the five steps, the current position and the Back/Next rules.
/// Steps with a page view model (Disks and Review) drive <see cref="IsCurrentStepValid"/> from that page; the other steps
/// keep the flag they had, until their pages exist.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
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

    public IReadOnlyList<WizardStep> Steps { get; }

    /// <summary>One item per step, for the step rail. Current and completed flags follow <see cref="CurrentIndex"/>.</summary>
    public IReadOnlyList<WizardStepItemViewModel> StepItems { get; }

    /// <summary>Page of step 1, or null when the shell was created without pages.</summary>
    public DisksStepViewModel? DisksStep { get; }

    /// <summary>Page of step 2, or null when the shell was created without pages.</summary>
    public ReviewStepViewModel? ReviewStep { get; }

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

    public bool CanGoBack =>
        CurrentIndex > 0 && CurrentStep.Kind is not (WizardStepKind.Progress or WizardStepKind.Result);

    public bool CanGoNext => CurrentIndex < Steps.Count - 1 && IsCurrentStepValid;

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        var enteringReview = Steps[CurrentIndex + 1].Kind == WizardStepKind.Review;
        if (enteringReview)
        {
            // Forget any plan from an earlier visit before the index changes, so Next stays disabled until the new plan exists.
            ReviewStep?.Clear();
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
        CurrentIndex--;
    }

    private void OnStepPagePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisksStepViewModel.IsValid) || e.PropertyName == nameof(ReviewStepViewModel.IsValid))
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
