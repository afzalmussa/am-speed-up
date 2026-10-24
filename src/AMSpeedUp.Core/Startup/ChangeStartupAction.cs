using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Undo;

namespace AMSpeedUp.Core.Startup;

/// <summary>Switch a startup item on or off. Always reversible from the Startup tab.</summary>
public sealed record ChangeStartupAction(StartupItem Item, bool TurnOn, string DisplayName) : PlannedAction
{
    public override string Summary => TurnOn ? $"Start {DisplayName} with Windows again" : $"Stop {DisplayName} starting with Windows";

    public override string? Detail => TurnOn ? null : "It isn't removed. You can still open it yourself whenever you like.";

    public override UndoSupport Undo => UndoSupport.InApp;

    public override ProtectionResult CheckProtection() => TurnOn ? ProtectionResult.NotProtected : StartupProtection.CheckTurningOff(Item);

    public override UndoEntry CreateUndoEntry(DateTimeOffset when) => new()
    {
        Id = Guid.NewGuid(),
        When = when,
        Kind = TurnOn ? UndoKind.StartupItemEnabled : UndoKind.StartupItemDisabled,
        DisplayName = DisplayName,
        Data = new Dictionary<string, string>
        {
            ["source"] = Item.Source.ToString(),
            ["name"] = Item.Name,
            ["taskPath"] = Item.TaskPath ?? "",
        },
        CanUndoInApp = true,
    };
}
