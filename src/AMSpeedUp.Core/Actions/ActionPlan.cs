using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Core.Actions;

public sealed record BlockedAction(PlannedAction Action, string Reason);

/// <summary>
/// A set of changes waiting for the user's go-ahead. The only way to run it is
/// plan → <see cref="CreatePreview"/> → <see cref="PlanPreview.Confirm"/> → <see cref="ActionRunner"/>,
/// so nothing can change without a preview and a confirmation (safety rule 1).
/// </summary>
public sealed class ActionPlan
{
    private ActionPlan(string title, IReadOnlyList<PlannedAction> actions, IReadOnlyList<BlockedAction> blocked)
    {
        Title = title;
        Actions = actions;
        Blocked = blocked;
    }

    /// <summary>Heading for the preview, for example "Remove 5 apps".</summary>
    public string Title { get; }

    public IReadOnlyList<PlannedAction> Actions { get; }

    /// <summary>Removals dropped because the item is on the protected list. Shown in the preview so nothing vanishes silently.</summary>
    public IReadOnlyList<BlockedAction> Blocked { get; }

    public bool NeedsRestorePoint => Actions.Any(a => a.NeedsRestorePoint);

    public bool IsEmpty => Actions.Count == 0;

    public static ActionPlan Create(string title, IEnumerable<PlannedAction> actions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        var kept = new List<PlannedAction>();
        var blocked = new List<BlockedAction>();
        foreach (var action in actions.Distinct())
        {
            if (action.CheckProtection() is { IsProtected: true } protection)
            {
                blocked.Add(new BlockedAction(action, protection.Reason!));
                continue;
            }

            kept.Add(action);
        }

        return new ActionPlan(title, kept, blocked);
    }

    public PlanPreview CreatePreview(bool dryRun) => new(this, dryRun);

    /// <summary>
    /// Tests only. Builds a plan without the protected-list filter, to prove the runner still
    /// refuses protected items if a plan ever reaches it some other way.
    /// </summary>
    internal static ActionPlan CreateWithoutProtectionCheckForTests(string title, IEnumerable<PlannedAction> actions) =>
        new(title, actions.ToList(), []);
}

public sealed record PreviewSection(UndoSupport Undo, string UndoNote, IReadOnlyList<PlannedAction> Actions);

/// <summary>Everything the user is shown before confirming: the checklist, grouped by how each change can be undone.</summary>
public sealed class PlanPreview
{
    private bool _confirmed;

    internal PlanPreview(ActionPlan plan, bool dryRun)
    {
        Plan = plan;
        IsDryRun = dryRun;

        // Most reversible first, so the scariest part is closest to the confirm button.
        Sections = plan.Actions
            .GroupBy(a => a.Undo)
            .OrderBy(g => g.Key)
            .Select(g => new PreviewSection(g.Key, SafetyText.UndoNote(g.Key), g.ToList()))
            .ToList();

        var notices = new List<string>();
        if (dryRun)
        {
            notices.Add(SafetyText.PreviewDryRunNotice);
        }

        if (plan.NeedsRestorePoint)
        {
            notices.Add(SafetyText.PreviewRestorePointNotice);
        }

        Notices = notices;
    }

    public ActionPlan Plan { get; }

    public bool IsDryRun { get; }

    public IReadOnlyList<PreviewSection> Sections { get; }

    public IReadOnlyList<string> Notices { get; }

    /// <summary>The user said yes. Can only be done once per preview.</summary>
    public ConfirmedPlan Confirm()
    {
        if (Plan.IsEmpty)
        {
            throw new InvalidOperationException("There is nothing to confirm.");
        }

        if (_confirmed)
        {
            throw new InvalidOperationException("This preview has already been confirmed.");
        }

        _confirmed = true;
        return new ConfirmedPlan(Plan, IsDryRun);
    }
}

/// <summary>A plan the user has seen and agreed to. Only <see cref="PlanPreview.Confirm"/> can create one.</summary>
public sealed class ConfirmedPlan
{
    internal ConfirmedPlan(ActionPlan plan, bool isDryRun)
    {
        Plan = plan;
        IsDryRun = isDryRun;
    }

    public ActionPlan Plan { get; }

    public bool IsDryRun { get; }

    internal bool HasRun { get; set; }
}
