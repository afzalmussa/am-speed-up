using System.Windows.Controls;
using AMSpeedUp.ViewModels;

namespace AMSpeedUp.Views;

public partial class FreeSpaceView : UserControl
{
    public FreeSpaceView()
    {
        InitializeComponent();

        // Check whether the monthly tune-up is on each time the tab is opened.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && DataContext is FreeSpaceViewModel { TuneUp: { } tuneUp })
            {
                await tuneUp.RefreshAsync();
            }
        };
    }
}
