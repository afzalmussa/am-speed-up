namespace AMSpeedUp.Services;

/// <summary>Shows windows and messages. Behind an interface so view models can be tested without a screen.</summary>
public interface IDialogService
{
    void ShowMessage(string title, string message);

    void ShowAbout();
}
