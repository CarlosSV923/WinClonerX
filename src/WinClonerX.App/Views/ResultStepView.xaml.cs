using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WinClonerX.App.ViewModels;

namespace WinClonerX.App.Views;

/// <summary>Step 5 view. The report is handed over by the wizard when the run ends.</summary>
public partial class ResultStepView : UserControl
{
    public ResultStepView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(FocusPrimaryAction, DispatcherPriority.Loaded);
    }

    private void FocusPrimaryAction()
    {
        if (Window.GetWindow(this) is not { } window)
        {
            return;
        }

        var targetName = DataContext is ResultStepViewModel { CanStartOver: true }
            ? "ResultStartOverButton"
            : "ResultFinishButton";
        (window.FindName(targetName) as System.Windows.Controls.Button)?.Focus();
    }
}
