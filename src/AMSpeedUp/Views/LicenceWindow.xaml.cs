using System.Windows;

namespace AMSpeedUp.Views;

public partial class LicenceWindow : Window
{
    public LicenceWindow()
    {
        InitializeComponent();
        MaxHeight = Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 16);
        Loaded += (_, _) =>
        {
            if (KeyBox.IsVisible)
            {
                KeyBox.Focus();
            }
        };
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
