using System.Windows;
using WinCopy.App.ViewModels;

namespace WinCopy.App;

/// <summary>Main window: the cloning wizard shell. Its DataContext comes from DI.</summary>
public partial class MainWindow : Window
{
    public MainWindow(WizardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
