using System.Windows;

namespace AMSpeedUp.Views;

public partial class MessageWindow : Window
{
    public MessageWindow(string heading, string message)
    {
        InitializeComponent();
        MaxHeight = Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 16);
        HeadingBlock.Text = heading;
        MessageBlock.Text = message;
    }

    public static void Show(Window? owner, string heading, string message)
    {
        var window = new MessageWindow(heading, message);
        if (owner is not null)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true;
        }

        window.ShowDialog();
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
