using System.Collections;
using System.Windows;
using System.Windows.Controls;

namespace WinClonerX.App.Controls;

/// <summary>Vertical list of the wizard steps: highlights the current one and marks the completed ones.</summary>
public partial class StepRail : UserControl
{
    public static readonly DependencyProperty StepItemsProperty = DependencyProperty.Register(
        nameof(StepItems),
        typeof(IEnumerable),
        typeof(StepRail),
        new PropertyMetadata(null));

    public StepRail()
    {
        InitializeComponent();
    }

    /// <summary>The step items to show, normally <c>WizardViewModel.StepItems</c>.</summary>
    public IEnumerable? StepItems
    {
        get => (IEnumerable?)GetValue(StepItemsProperty);
        set => SetValue(StepItemsProperty, value);
    }
}
