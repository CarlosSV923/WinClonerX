using CommunityToolkit.Mvvm.ComponentModel;
using WinClonerX.Domain.Planning;

namespace WinClonerX.App.ViewModels;

/// <summary>One warning of the plan, with the "I understand" checkbox that must be ticked before continuing.</summary>
public sealed partial class WarningItemViewModel : ObservableObject
{
    public WarningItemViewModel(PreflightCode code, string text)
    {
        Code = code;
        Text = text;
    }

    public PreflightCode Code { get; }

    /// <summary>Localized explanation of the warning.</summary>
    public string Text { get; }

    [ObservableProperty]
    private bool _isAccepted;
}
