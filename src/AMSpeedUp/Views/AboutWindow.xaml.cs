using System.Windows;

namespace AMSpeedUp.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowSizing.FitToWorkArea(this);
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
