using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Undo;

namespace AMSpeedUp.Core.Actions;

/// <summary>How a change can be undone. Shown honestly in the preview before the user confirms (safety rule 7).</summary>
public enum UndoSupport
{
    /// <summary>The app can undo it, for example turning a startup item back on.</summary>
    InApp,

    /// <summary>Removed Store apps can usually be reinstalled from the app or the Microsoft Store.</summary>
    ReinstallFromApp,

    /// <summary>Removed desktop programs can only come back by using the restore point.</summary>
    RestorePointOnly,

    /// <summary>Deleted files are gone for good.</summary>
    CannotUndo,
}

/// <summary>One change the user will see in the preview and confirm before it happens.</summary>
public abstract record PlannedAction
{
    /// <summary>One line for the preview checklist, for example "Remove Candy Crush Saga".</summary>
    public abstract string Summary { get; }

    /// <summary>An optional second line with more detail.</summary>
    public virtual string? Detail => null;

    public abstract UndoSupport Undo { get; }

    /// <summary>Removals need a restore point first (safety rule 2). Startup changes and file cleanup don't.</summary>
    public virtual bool NeedsRestorePoint => Undo is UndoSupport.ReinstallFromApp or UndoSupport.RestorePointOnly;

    /// <summary>What to record so the change can be undone or explained later. Null if nothing needs recording.</summary>
    public virtual UndoEntry? CreateUndoEntry(DateTimeOffset when) => null;
}

/// <summary>Remove a Store app or desktop program.</summary>
public sealed record RemoveAppAction(InstalledApp App, PresetEntry? Entry = null) : PlannedAction
{
    /// <summary>
    /// The order removals run in: Store apps, then silent removals, then ones the user clicks
    /// through, and antivirus last. Antivirus tools often ask for a restart, so everything else is
    /// finished first, and the PC keeps its antivirus for as long as possible.
    /// </summary>
    public static IReadOnlyList<RemoveAppAction> InSafeOrder(IEnumerable<RemoveAppAction> actions) =>
        actions
            .OrderBy(a => a.IsAntivirus)
            .ThenBy(a => a.App.Kind == AppKind.StoreApp ? 0 : a.Method.Kind switch
            {
                UninstallKind.Silent => 1,
                UninstallKind.Interactive => 2,
                UninstallKind.VendorTool => 3,
                _ => 4,
            })
            .ThenBy(a => a.App.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    public override string Summary => $"Remove {App.DisplayName}";

    public override string? Detail
    {
        get
        {
            if (App.Kind == AppKind.StoreApp)
            {
                return App.PackageFullNames.Count == 0 && App.ProvisionedPackageName is not null
                    ? "Stops it being added for new user accounts on this PC."
                    : null;
            }

            return Method.Kind switch
            {
                UninstallKind.VendorTool => $"Uses {VendorName}'s own removal tool. It will open and ask you to click through a few steps.",
                UninstallKind.Interactive => "Its own uninstaller will open. Follow its steps to finish removing it.",
                UninstallKind.None => "Windows doesn't say how to remove this, so AM Speed Up can't remove it.",
                _ => null,
            };
        }
    }

    /// <summary>How a desktop program will be removed. Store apps don't use this.</summary>
    public UninstallMethod Method => UninstallMethods.Choose(App, VendorTool);

    public string VendorName => VendorTool switch
    {
        VendorRemovalTool.McAfee => "McAfee",
        VendorRemovalTool.Norton => "Norton",
        _ => App.Publisher ?? App.DisplayName,
    };

    public override UndoSupport Undo =>
        App.Kind == AppKind.StoreApp ? UndoSupport.ReinstallFromApp : UndoSupport.RestorePointOnly;

    /// <summary>Triggers the Microsoft Defender check afterwards (safety rule 4).</summary>
    public bool IsAntivirus => Entry?.IsAntivirus ?? false;

    public VendorRemovalTool VendorTool => Entry?.VendorTool ?? VendorRemovalTool.None;

    public override UndoEntry CreateUndoEntry(DateTimeOffset when)
    {
        var data = new Dictionary<string, string> { ["id"] = App.Id };
        if (App.PackageFamilyName is not null)
        {
            data["packageFamilyName"] = App.PackageFamilyName;
        }

        if (App.Publisher is not null)
        {
            data["publisher"] = App.Publisher;
        }

        return new UndoEntry
        {
            Id = Guid.NewGuid(),
            When = when,
            Kind = App.Kind == AppKind.StoreApp ? UndoKind.StoreAppRemoved : UndoKind.DesktopProgramRemoved,
            DisplayName = App.DisplayName,
            Data = data,
            CanUndoInApp = App.Kind == AppKind.StoreApp,
        };
    }
}
