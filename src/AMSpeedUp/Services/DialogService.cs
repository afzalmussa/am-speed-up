using System.Windows;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.Services;

public sealed class DialogService : IDialogService
{
    // Set after construction because the view model and this service need each other.
    public MainViewModel? MainViewModel { get; set; }

    public void ShowMessage(string title, string message) =>
        MessageWindow.Show(Owner, title, message);

    public void ShowAbout()
    {
        var about = new AboutWindow { DataContext = MainViewModel, Owner = Owner };
        about.ShowDialog();
    }

    private static Window? Owner =>
        Application.Current?.MainWindow is { IsVisible: true } main ? main : null;
}
