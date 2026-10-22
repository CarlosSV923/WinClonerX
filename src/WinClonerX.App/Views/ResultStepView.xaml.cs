using System.Windows.Controls;
using System.Windows.Threading;

namespace WinClonerX.App.Views;

/// <summary>Step 5 view. The report is handed over by the wizard when the run ends.</summary>
public partial class ResultStepView : UserControl
{
    public ResultStepView()
    {
        InitializeComponent();
        Loaded += (_, _) => Dispatcher.BeginInvoke(() => ExportLogButton.Focus(), DispatcherPriority.Loaded);
    }
}
