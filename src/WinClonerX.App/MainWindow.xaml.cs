using System.ComponentModel;
using System.Windows;
using WinClonerX.App.ViewModels;

namespace WinClonerX.App;

/// <summary>Main window: the cloning wizard shell. Its DataContext comes from DI.</summary>
public partial class MainWindow : Window
{
    /// <summary>True once the cancel is in effect and the window closes when the run ends.</summary>
    private bool _closeWhenRunEnds;

    public MainWindow(WizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    /// <summary>
    /// Closing the window during a run is the same as Cancel (FR-024). The window stays open until the run has finished its
    /// compensations, then closes. If the person declines the confirmation, it stays open. When no run is in progress, it closes
    /// normally.
    /// </summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);

        if (DataContext is not WizardViewModel { IsExecutionRunning: true } viewModel)
        {
            return;
        }

        e.Cancel = true;
        if (_closeWhenRunEnds || !viewModel.RequestCancelForClose())
        {
            return;
        }

        _closeWhenRunEnds = true;
        _ = CloseWhenRunEndsAsync(viewModel.ExecutionCompletion);
    }

    private async Task CloseWhenRunEndsAsync(Task run)
    {
        await run;
        Close();
    }
}
