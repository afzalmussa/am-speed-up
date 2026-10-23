using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Presets;

namespace AMSpeedUp.UI.Tests;

/// <summary>Realistic plans for the preview window tests and screenshots.</summary>
internal static class SampleData
{
    public static InstalledApp Store(string id, string name) => new()
    {
        Kind = AppKind.StoreApp,
        Id = id,
        DisplayName = name,
        PackageFamilyName = id + "_8wekyb3d8bbwe",
        SignatureKind = PackageSignatureKind.Store,
    };

    public static InstalledApp Desktop(string name, string publisher) => new()
    {
        Kind = AppKind.DesktopProgram,
        Id = "uninstall-" + name,
        DisplayName = name,
        Publisher = publisher,
        UninstallString = $"\"C:\\Program Files\\{name}\\uninstall.exe\"",
    };

    public static PlanPreview MixedPreview(bool dryRun = false)
    {
        var mcafee = new PresetEntry
        {
            Kind = AppKind.DesktopProgram,
            Match = "McAfee*",
            Name = "McAfee",
            Description = "Trial antivirus",
            Recommendation = Recommendation.Remove,
            VendorTool = VendorRemovalTool.McAfee,
            IsAntivirus = true,
        };

        PlannedAction[] actions =
        [
            new SampleStartupAction("Spotify"),
            new SampleStartupAction("Microsoft Teams"),
            new RemoveAppAction(Store("king.com.CandyCrushSaga", "Candy Crush Saga")),
            new RemoveAppAction(Store("Microsoft.BingNews", "Microsoft News")),
            new RemoveAppAction(Desktop("McAfee LiveSafe", "McAfee, LLC"), mcafee),
            new RemoveAppAction(Desktop("WildTangent Games", "WildTangent")),
            new SampleCleanupAction("Temporary files"),
            new RemoveAppAction(Store("Microsoft.WindowsStore", "Microsoft Store")),
        ];

        return ActionPlan.Create("Remove apps and tidy up", actions).CreatePreview(dryRun);
    }

    public sealed record SampleStartupAction(string Name) : PlannedAction
    {
        public override string Summary => $"Stop {Name} starting with Windows";

        public override UndoSupport Undo => UndoSupport.InApp;
    }

    public sealed record SampleCleanupAction(string What) : PlannedAction
    {
        public override string Summary => $"Delete {What}";

        public override UndoSupport Undo => UndoSupport.CannotUndo;
    }
}
