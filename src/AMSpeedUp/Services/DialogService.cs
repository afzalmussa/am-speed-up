using System.Windows;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.Services;

public sealed class DialogService(IShellLauncher shell) : IDialogService
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

    public bool ConfirmPlan(PlanPreview preview)
    {
        var window = new PreviewWindow(preview);
        if (Owner is { } owner)
        {
            window.Owner = owner;
        }

        window.ShowDialog();
        return window.Confirmed;
    }

    public bool AskContinueWithoutRestorePoint(RestorePointResult result) =>
        MeansContinue(result.Status, ChoiceWindow.Show(Owner, RestorePointChoice(result, shell)));

    public void ShowAntivirusWarning(AntivirusCheckResult result) =>
        ChoiceWindow.Show(Owner, AntivirusChoice(result, shell));

    /// <summary>
    /// Restore point problems. Where there's no restore point at all, Cancel is the main button so
    /// pressing Enter is always the safe choice. If a recent one exists, carrying on is reasonable.
    /// </summary>
    public static Choice RestorePointChoice(RestorePointResult result, IShellLauncher shell)
    {
        var message = SafetyText.RestorePointProblem(result);
        return result.Status switch
        {
            RestorePointStatus.TooSoon => new Choice(message.Heading, message.Body,
                PrimaryText: SafetyText.ContinueWithExistingRestorePoint, SecondaryText: "Cancel"),

            RestorePointStatus.TurnedOff => new Choice(message.Heading, message.Body,
                PrimaryText: "Cancel", SecondaryText: SafetyText.ContinueWithoutRestorePoint,
                ExtraText: SafetyText.TurnOnSystemRestore, ExtraAction: () => shell.OpenWindowsPage(WindowsPage.SystemProtection),
                IsWarning: true),

            _ => new Choice(message.Heading, message.Body,
                PrimaryText: "Cancel", SecondaryText: SafetyText.ContinueWithoutRestorePoint, IsWarning: true),
        };
    }

    /// <summary>True when the button the user clicked means "carry on without a new restore point".</summary>
    public static bool MeansContinue(RestorePointStatus status, ChoiceResult choice) => status == RestorePointStatus.TooSoon
        ? choice == ChoiceResult.Primary
        : choice == ChoiceResult.Secondary;

    public static Choice AntivirusChoice(AntivirusCheckResult result, IShellLauncher shell)
    {
        var message = SafetyText.AntivirusWarning(result);
        return new Choice(message.Heading, message.Body,
            PrimaryText: "Close",
            ExtraText: SafetyText.OpenWindowsSecurity,
            ExtraAction: () => shell.OpenWindowsPage(WindowsPage.VirusProtection),
            IsWarning: true);
    }

    private static Window? Owner =>
        Application.Current?.MainWindow is { IsVisible: true } main ? main : null;
}
