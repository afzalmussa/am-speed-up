using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Services;

/// <summary>Shows windows and messages. Behind an interface so view models can be tested without a screen.</summary>
public interface IDialogService
{
    void ShowMessage(string title, string message);

    void ShowAbout();

    /// <summary>Shows the preview checklist. Returns true only if the user clicked the confirm button.</summary>
    bool ConfirmPlan(PlanPreview preview);

    /// <summary>Explains why there's no new restore point. Returns true only if the user chose to carry on.</summary>
    bool AskContinueWithoutRestorePoint(RestorePointResult result);

    void ShowAntivirusWarning(AntivirusCheckResult result);
}
