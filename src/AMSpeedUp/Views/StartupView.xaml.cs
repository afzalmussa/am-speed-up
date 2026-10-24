using System.Windows;
using System.Windows.Controls;
using AMSpeedUp.ViewModels;

namespace AMSpeedUp.Views;

public partial class StartupView : UserControl
{
    public StartupView()
    {
        InitializeComponent();

        // Load the list the first time the tab is shown, not when the app opens.
        IsVisibleChanged += async (_, e) =>
        {
            if (e.NewValue is true && DataContext is StartupViewModel viewModel)
            {
                await viewModel.EnsureLoadedAsync();
            }
        };
    }
}
