using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Actions;

public class RecommendedTests
{
    private static MatchedApp App(string name, Recommendation recommendation = Recommendation.Remove, bool locked = false, bool antivirus = false) =>
        new(Apps.Desktop(name), Apps.Entry(AppKind.DesktopProgram, name, recommendation, antivirus: antivirus), recommendation, locked);

    private static (StartupItem, StartupDescription) Startup(string name, bool on = true, StartupAdvice? advice = StartupAdvice.TurnOff, bool protectedItem = false) =>
        (new StartupItem { Source = StartupSource.RunCurrentUser, Name = name, IsEnabled = on },
         new StartupDescription(name, "", advice, protectedItem ? ProtectionResult.Because("Needed by Windows") : ProtectionResult.NotProtected));

    private static CleanupTarget Clutter(CleanupCategory category, long bytes = 1_000_000, string? blocked = null) =>
        new() { Category = category, Folders = [], Bytes = bytes, Files = 1, BlockedReason = blocked };

    [Fact]
    public void Only_apps_recommended_for_removal_are_included()
    {
        var actions = Recommended.Build(
            [App("WildTangent Games"), App("Spotify", Recommendation.Optional), App("HP Support Assistant", Recommendation.Keep)],
            [], []);

        Assert.Equal(["Remove WildTangent Games"], actions.Select(a => a.Summary));
    }

    [Fact]
    public void Pro_apps_that_are_locked_on_the_free_version_are_left_out()
    {
        var actions = Recommended.Build([App("HP Wolf Security", locked: true)], [], []);

        Assert.Empty(actions);
    }

    [Fact]
    public void Antivirus_is_removed_last_like_on_the_Remove_bloat_tab()
    {
        var actions = Recommended.Build([App("McAfee LiveSafe", antivirus: true), App("WildTangent Games")], [], []);

        Assert.Equal(["Remove WildTangent Games", "Remove McAfee LiveSafe"], actions.Select(a => a.Summary));
    }

    [Fact]
    public void Only_startup_programs_that_are_on_and_can_be_turned_off_are_included()
    {
        var actions = Recommended.Build([],
        [
            Startup("Spotify"),
            Startup("Teams", on: false),
            Startup("OneDrive", advice: StartupAdvice.Optional),
            Startup("Realtek Audio", advice: StartupAdvice.KeepOn),
            Startup("Unknown thing", advice: null),
            Startup("Security notification icon", protectedItem: true),
        ], []);

        var change = Assert.IsType<ChangeStartupAction>(Assert.Single(actions));
        Assert.Equal("Spotify", change.DisplayName);
        Assert.False(change.TurnOn);
    }

    [Fact]
    public void Clean_up_matches_the_tune_up_and_never_empties_the_Recycle_Bin()
    {
        var actions = Recommended.Build([], [],
        [
            Clutter(CleanupCategory.UserTemp),
            Clutter(CleanupCategory.RecycleBin, 5_000_000_000),
            Clutter(CleanupCategory.EdgeCache, blocked: "Microsoft Edge is open"),
            Clutter(CleanupCategory.WindowsTemp, bytes: 0),
        ]);

        Assert.Equal([CleanupCategory.UserTemp], actions.Cast<CleanupAction>().Select(a => a.Target.Category));
    }

    [Fact]
    public void Startup_changes_come_first_then_removals_then_clean_up()
    {
        var actions = Recommended.Build([App("WildTangent Games")], [Startup("Spotify")], [Clutter(CleanupCategory.UserTemp)]);

        Assert.Collection(actions,
            a => Assert.IsType<ChangeStartupAction>(a),
            a => Assert.IsType<RemoveAppAction>(a),
            a => Assert.IsType<CleanupAction>(a));
    }

    [Fact]
    public void The_plan_still_goes_through_the_protected_list()
    {
        var actions = Recommended.Build([new MatchedApp(Apps.Store("Microsoft.WindowsStore", "Microsoft Store"), Apps.Entry(AppKind.StoreApp, "Microsoft.WindowsStore"), Recommendation.Remove, false)], [], []);

        var plan = ActionPlan.Create(Recommended.PlanTitle, actions);

        Assert.True(plan.IsEmpty);
        Assert.Single(plan.Blocked);
    }
}
