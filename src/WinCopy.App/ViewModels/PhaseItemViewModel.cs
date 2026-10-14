using CommunityToolkit.Mvvm.ComponentModel;
using WinCopy.App.Localization;
using WinCopy.Domain.Execution;

namespace WinCopy.App.ViewModels;

/// <summary>State of one phase in the progress list. The text repeats it, so the meaning does not depend on color.</summary>
public enum PhaseState
{
    Pending,
    InProgress,
    Done,
}

/// <summary>One phase of the clone in the progress list: its localized name and its state.</summary>
public sealed partial class PhaseItemViewModel : ObservableObject
{
    public PhaseItemViewModel(ExecutionPhase phase)
    {
        Phase = phase;
        Name = Loc.Get("Phase_" + phase);
    }

    public ExecutionPhase Phase { get; }

    /// <summary>Localized phase name, for example "Copying data".</summary>
    public string Name { get; }

    [ObservableProperty]
    private PhaseState _state = PhaseState.Pending;

    /// <summary>Localized state: pending, in progress or done.</summary>
    public string StateText => State switch
    {
        PhaseState.InProgress => Loc.Get("Progress_State_InProgress"),
        PhaseState.Done => Loc.Get("Progress_State_Done"),
        _ => Loc.Get("Progress_State_Pending"),
    };

    partial void OnStateChanged(PhaseState value) => OnPropertyChanged(nameof(StateText));
}
