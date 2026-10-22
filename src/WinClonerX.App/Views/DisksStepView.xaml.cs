using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WinClonerX.App.ViewModels;

namespace WinClonerX.App.Views;

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
        Unloaded += OnUnloaded;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not DisksStepViewModel viewModel)
        {
            return;
        }

        viewModel.PropertyChanged += ViewModel_PropertyChanged;
        if (!viewModel.IsLoaded && !viewModel.IsLoading)
        {
            viewModel.LoadCommand.Execute(null);
        }
        else
        {
            FocusInitialTarget();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is DisksStepViewModel viewModel)
        {
            viewModel.PropertyChanged -= ViewModel_PropertyChanged;
        }
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
        {
            Dispatcher.BeginInvoke(FocusInitialTarget, DispatcherPriority.Loaded);
        }
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DisksStepViewModel.IsLoaded) && DataContext is DisksStepViewModel { IsLoaded: true })
        {
            Dispatcher.BeginInvoke(FocusInitialTarget, DispatcherPriority.Loaded);
        }
    }

    private void FocusInitialTarget()
    {
        if (!IsVisible)
        {
            return;
        }

        TargetList.UpdateLayout();
        var firstSelectable = TargetList.Items
            .OfType<TargetDiskItemViewModel>()
            .FirstOrDefault(target => target.IsSelectable);
        if (firstSelectable is not null && TargetList.ItemContainerGenerator.ContainerFromItem(firstSelectable) is ListBoxItem item)
        {
            item.Focus();
            return;
        }

        if (TargetList.IsVisible)
        {
            TargetList.Focus();
            return;
        }

        FindFocusableButton(this)?.Focus();
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

    private static Button? FindFocusableButton(DependencyObject root)
    {
        if (root is Button { IsVisible: true, IsEnabled: true } button)
        {
            return button;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
        {
            if (FindFocusableButton(VisualTreeHelper.GetChild(root, index)) is { } child)
            {
                return child;
            }
        }

        return null;
    }
}
