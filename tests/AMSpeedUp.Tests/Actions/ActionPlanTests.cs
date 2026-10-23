using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Actions;

public class ActionPlanTests
{
    private static readonly RemoveAppAction RemoveCandy = new(Apps.Store("king.com.CandyCrushSaga", "Candy Crush Saga"));
    private static readonly RemoveAppAction RemoveWildTangent = new(Apps.Desktop("WildTangent Games"));
    private static readonly FakeStartupAction StartupSpotify = new("Spotify");
    private static readonly FakeCleanupAction TempFiles = new("temporary files");

    [Fact]
    public void Protected_items_are_taken_out_of_the_plan_and_listed_as_blocked()
    {
        var store = new RemoveAppAction(Apps.Store("Microsoft.WindowsStore", "Microsoft Store"));

        var plan = ActionPlan.Create("Remove apps", [RemoveCandy, store]);

        Assert.Equal([RemoveCandy], plan.Actions);
        var blocked = Assert.Single(plan.Blocked);
        Assert.Equal(store, blocked.Action);
        Assert.Contains("Microsoft Store", blocked.Reason);
    }

    [Fact]
    public void Duplicate_actions_are_only_planned_once()
    {
        var plan = ActionPlan.Create("Remove apps", [RemoveCandy, RemoveCandy with { }]);

        Assert.Single(plan.Actions);
    }

    [Fact]
    public void A_plan_needs_a_title()
    {
        Assert.Throws<ArgumentException>(() => ActionPlan.Create(" ", [RemoveCandy]));
    }

    [Fact]
    public void Removals_need_a_restore_point_but_startup_changes_and_cleanup_do_not()
    {
        Assert.True(ActionPlan.Create("t", [RemoveCandy]).NeedsRestorePoint);
        Assert.True(ActionPlan.Create("t", [RemoveWildTangent]).NeedsRestorePoint);
        Assert.False(ActionPlan.Create("t", [StartupSpotify, TempFiles]).NeedsRestorePoint);
        Assert.True(ActionPlan.Create("t", [StartupSpotify, RemoveWildTangent]).NeedsRestorePoint);
    }

    [Fact]
    public void Undo_is_described_honestly_for_each_kind_of_change()
    {
        Assert.Equal(UndoSupport.ReinstallFromApp, RemoveCandy.Undo);
        Assert.Equal(UndoSupport.RestorePointOnly, RemoveWildTangent.Undo);
        Assert.Equal(UndoSupport.InApp, StartupSpotify.Undo);
        Assert.Equal(UndoSupport.CannotUndo, TempFiles.Undo);
    }

    [Fact]
    public void Preview_groups_changes_by_how_they_can_be_undone_most_reversible_first()
    {
        var preview = ActionPlan.Create("Everything", [TempFiles, RemoveWildTangent, RemoveCandy, StartupSpotify]).CreatePreview(dryRun: false);

        Assert.Equal(
            [UndoSupport.InApp, UndoSupport.ReinstallFromApp, UndoSupport.RestorePointOnly, UndoSupport.CannotUndo],
            preview.Sections.Select(s => s.Undo));
        Assert.All(preview.Sections, s => Assert.Equal(SafetyText.UndoNote(s.Undo), s.UndoNote));
        Assert.Equal([RemoveWildTangent], preview.Sections[2].Actions);
    }

    [Fact]
    public void Preview_says_desktop_programs_can_only_come_back_with_the_restore_point()
    {
        var preview = ActionPlan.Create("t", [RemoveWildTangent]).CreatePreview(dryRun: false);

        Assert.Contains("restore point", Assert.Single(preview.Sections).UndoNote);
        Assert.Contains("can't put these back", preview.Sections[0].UndoNote);
    }

    [Fact]
    public void Preview_mentions_the_restore_point_only_when_one_will_be_made()
    {
        Assert.Contains(SafetyText.PreviewRestorePointNotice, ActionPlan.Create("t", [RemoveCandy]).CreatePreview(false).Notices);
        Assert.DoesNotContain(SafetyText.PreviewRestorePointNotice, ActionPlan.Create("t", [StartupSpotify]).CreatePreview(false).Notices);
    }

    [Fact]
    public void Preview_says_when_test_mode_is_on()
    {
        Assert.Contains(SafetyText.PreviewDryRunNotice, ActionPlan.Create("t", [RemoveCandy]).CreatePreview(dryRun: true).Notices);
        Assert.DoesNotContain(SafetyText.PreviewDryRunNotice, ActionPlan.Create("t", [RemoveCandy]).CreatePreview(dryRun: false).Notices);
    }

    [Fact]
    public void A_preview_can_only_be_confirmed_once()
    {
        var preview = ActionPlan.Create("t", [RemoveCandy]).CreatePreview(false);

        var confirmed = preview.Confirm();

        Assert.Same(preview.Plan, confirmed.Plan);
        Assert.Throws<InvalidOperationException>(() => preview.Confirm());
    }

    [Fact]
    public void An_empty_plan_cannot_be_confirmed()
    {
        var store = new RemoveAppAction(Apps.Store("Microsoft.WindowsStore"));
        var preview = ActionPlan.Create("t", [store]).CreatePreview(false);

        Assert.True(preview.Plan.IsEmpty);
        Assert.Throws<InvalidOperationException>(() => preview.Confirm());
    }

    [Fact]
    public void Confirmation_carries_the_dry_run_setting_from_the_preview()
    {
        Assert.True(ActionPlan.Create("t", [RemoveCandy]).CreatePreview(dryRun: true).Confirm().IsDryRun);
        Assert.False(ActionPlan.Create("t", [RemoveCandy]).CreatePreview(dryRun: false).Confirm().IsDryRun);
    }

    [Fact]
    public void Vendor_tool_removals_say_so_in_the_preview()
    {
        var entry = Apps.Entry(AppKind.DesktopProgram, "McAfee*", tool: VendorRemovalTool.McAfee);
        var action = new RemoveAppAction(Apps.Desktop("McAfee LiveSafe"), entry);

        Assert.Equal("Uses McAfee's own removal tool.", action.Detail);
        Assert.True(action.IsAntivirus);
        Assert.Null(RemoveCandy.Detail);
    }

    [Fact]
    public void Removing_a_store_app_records_what_is_needed_to_reinstall_it()
    {
        var when = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero);

        var entry = RemoveCandy.CreateUndoEntry(when);

        Assert.Equal(UndoKind.StoreAppRemoved, entry.Kind);
        Assert.True(entry.CanUndoInApp);
        Assert.Equal(when, entry.When);
        Assert.Equal("Candy Crush Saga", entry.DisplayName);
        Assert.Equal("king.com.CandyCrushSaga", entry.Data["id"]);
        Assert.Equal("king.com.CandyCrushSaga_8wekyb3d8bbwe", entry.Data["packageFamilyName"]);
    }

    [Fact]
    public void Removing_a_desktop_program_is_recorded_as_not_undoable_in_the_app()
    {
        var entry = RemoveWildTangent.CreateUndoEntry(DateTimeOffset.UnixEpoch);

        Assert.Equal(UndoKind.DesktopProgramRemoved, entry.Kind);
        Assert.False(entry.CanUndoInApp);
    }
}
