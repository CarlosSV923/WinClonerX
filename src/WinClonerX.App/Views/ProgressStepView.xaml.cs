using System.Windows.Controls;
using System.Windows.Threading;

namespace WinClonerX.App.Views;

/// <summary>Step 4 view. The run is started by the wizard, so the view only shows the state of the view model.</summary>
public partial class ProgressStepView : UserControl
{
    public ProgressStepView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => CancelButton.Focus(), DispatcherPriority.Loaded);
    }
}
