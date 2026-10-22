using System.Windows.Controls;

namespace WinClonerX.App.Controls;

/// <summary>
/// Card for one disk: model, serial number, capacity, connection, partition bar, and for destinations the eligibility badge,
/// the reasons and the strategy hint. Its DataContext is a <see cref="ViewModels.DiskCardViewModel"/>.
/// </summary>
public partial class DiskCard : UserControl
{
    public DiskCard()
    {
        InitializeComponent();
    }
}
