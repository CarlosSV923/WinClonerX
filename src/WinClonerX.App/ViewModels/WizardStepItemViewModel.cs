using CommunityToolkit.Mvvm.ComponentModel;

namespace WinClonerX.App.ViewModels;

/// <summary>View model for one entry of the step rail: its position and whether it is current or done.</summary>
public sealed partial class WizardStepItemViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isCompleted;

    public WizardStepItemViewModel(WizardStep step, int number)
    {
        Step = step;
        Number = number;
    }

    public WizardStep Step { get; }

    /// <summary>1-based position shown in the rail.</summary>
    public int Number { get; }

    public string TitleKey => Step.TitleKey;
}
