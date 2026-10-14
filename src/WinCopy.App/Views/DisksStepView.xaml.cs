using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WinCopy.App.ViewModels;

namespace WinCopy.App.Views;

/// <summary>
/// Step 1 view. Loads the disks the first time it is shown, and lets the keyboard select a destination: Space or Enter
/// on a focused destination selects it. Arrow keys move between destinations and skip the ones that are not eligible.
/// </summary>
public partial class DisksStepView : UserControl
{
    public DisksStepView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DisksStepViewModel { IsLoaded: false, IsLoading: false } viewModel)
        {
            viewModel.LoadCommand.Execute(null);
        }
    }

    private void TargetList_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Space) || DataContext is not DisksStepViewModel viewModel)
        {
            return;
        }

        if (FindAncestor<ListBoxItem>(e.OriginalSource as DependencyObject) is { DataContext: TargetDiskItemViewModel target }
            && viewModel.SelectTargetCommand.CanExecute(target))
        {
            viewModel.SelectTargetCommand.Execute(target);
            e.Handled = true;
        }
    }

    private static T? FindAncestor<T>(DependencyObject? start)
        where T : DependencyObject
    {
        for (var current = start; current is not null; current = VisualTreeHelper.GetParent(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }
}
