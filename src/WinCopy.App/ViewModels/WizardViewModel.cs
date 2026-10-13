using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace WinCopy.App.ViewModels;

/// <summary>
/// Navigation shell of the cloning wizard: the five steps, the current position and the Back/Next rules.
/// Step pages set <see cref="IsCurrentStepValid"/> once they exist; until then it stays true.
/// </summary>
public sealed partial class WizardViewModel : ObservableObject
{
    private int _currentIndex;
    private bool _isCurrentStepValid = true;

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

    public IReadOnlyList<WizardStep> Steps { get; }

    /// <summary>One item per step, for the step rail. Current and completed flags follow <see cref="CurrentIndex"/>.</summary>
    public IReadOnlyList<WizardStepItemViewModel> StepItems { get; }

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
            OnPropertyChanged(nameof(CanGoBack));
            OnPropertyChanged(nameof(CanGoNext));
            BackCommand.NotifyCanExecuteChanged();
            NextCommand.NotifyCanExecuteChanged();
            UpdateStepItems();
        }
    }

    public WizardStep CurrentStep => Steps[CurrentIndex];

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
    private void Next()
    {
        CurrentIndex++;
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back()
    {
        CurrentIndex--;
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
