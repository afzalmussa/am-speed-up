using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Startup;

namespace AMSpeedUp.Core.Actions;

/// <summary>
/// Pro: "Do everything recommended" puts the advice from all three tabs into one plan, shown in
/// one preview. It only ever includes what each tab would tick or suggest on its own.
/// </summary>
public static class Recommended
{
    public const string PlanTitle = "Do everything recommended";

    public static IReadOnlyList<PlannedAction> Build(
        IEnumerable<MatchedApp> apps,
        IEnumerable<(StartupItem Item, StartupDescription Description)> startup,
        IEnumerable<CleanupTarget> cleanup)
    {
        // Startup changes first: they're quick and can be undone, and an app removed later in the
        // plan takes its own startup entry with it anyway.
        var startupChanges = startup
            .Where(s => s.Item.IsEnabled && s.Description.Advice == StartupAdvice.TurnOff && !s.Description.Protection.IsProtected)
            .Select(s => (PlannedAction)new ChangeStartupAction(s.Item, TurnOn: false, s.Description.DisplayName));

        var removals = RemoveAppAction.InSafeOrder(apps
            .Where(a => a.IsTickedByDefault)
            .Select(a => new RemoveAppAction(a.App with { DisplayName = a.DisplayName }, a.Entry)));

        // The same safe clean-ups as the monthly tune-up, which never includes the Recycle Bin.
        return [.. startupChanges, .. removals, .. TuneUp.BuildActions(cleanup)];
    }
}
