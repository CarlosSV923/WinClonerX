using System.Windows;
using System.Windows.Controls;

namespace WinCopy.App.Views;

/// <summary>
/// Step 3 view. Puts the keyboard focus in the confirmation field each time the step is shown, so the person can type
/// the characters and press Enter (the clone button is the default button).
/// </summary>
public partial class ConfirmStepView : UserControl
{
    public ConfirmStepView()
    {
        InitializeComponent();
        Loaded += (_, _) => SuffixBox.Focus();
    }
}
