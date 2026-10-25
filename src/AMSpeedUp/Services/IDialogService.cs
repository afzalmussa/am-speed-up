using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.VendorTools;

namespace AMSpeedUp.Services;

/// <summary>Shows windows and messages. Behind an interface so view models can be tested without a screen.</summary>
public interface IDialogService
{
    void ShowMessage(string title, string message);

    void ShowAbout();

    void ShowLicence();

    /// <summary>Explains that a feature needs Pro, with ways to buy it or enter a key.</summary>
    void ShowProRequired(string feature);

    /// <summary>A yes/no question. The "no" button is the main one, so pressing Enter is the safe choice.</summary>
    bool Confirm(string heading, string body, string yesText, string noText);

    /// <summary>Shows the preview checklist. Returns true only if the user clicked the confirm button.</summary>
    bool ConfirmPlan(PlanPreview preview);

    /// <summary>Explains why there's no new restore point. Returns true only if the user chose to carry on.</summary>
    bool AskContinueWithoutRestorePoint(RestorePointResult result);

    void ShowAntivirusWarning(AntivirusCheckResult result);

    /// <summary>Explains the steps in a vendor's removal tool. Returns true if the user wants to go ahead.</summary>
    bool ConfirmVendorTool(VendorToolInfo tool);

    void ShowVendorToolProblem(VendorToolInfo tool, string reason);

    /// <summary>Says where the report was saved, with buttons to open it or show it in its folder.</summary>
    void ShowReportSaved(string path);
}
