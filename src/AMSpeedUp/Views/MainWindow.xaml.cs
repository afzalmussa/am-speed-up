using System.Windows;

namespace AMSpeedUp.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowSizing.FitToWorkArea(this);
    }
}
