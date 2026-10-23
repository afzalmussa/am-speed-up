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
    public override string Summary => $"Remove {App.DisplayName}";

    public override string? Detail => VendorTool switch
    {
        VendorRemovalTool.McAfee => "Uses McAfee's own removal tool.",
        VendorRemovalTool.Norton => "Uses Norton's own removal tool.",
        _ => null,
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
