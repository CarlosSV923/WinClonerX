using System.Windows.Controls;

namespace WinClonerX.App.Views;

/// <summary>Step 2 view. The plan is built by the view model when the wizard enters this step.</summary>
public partial class ReviewStepView : UserControl
{
    public ReviewStepView()
    {
        InitializeComponent();
        Loaded += (_, _) => StepHeading.Focus();
    }
}
