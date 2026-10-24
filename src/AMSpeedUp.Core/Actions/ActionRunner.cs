using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Undo;

namespace AMSpeedUp.Core.Actions;

public enum OutcomeStatus
{
    Succeeded,
    Failed,

    /// <summary>Not done on purpose, for example because the item is protected.</summary>
    Skipped,

    /// <summary>Dry run: logged what would have happened, changed nothing.</summary>
    DryRun,

    /// <summary>The run was cancelled before this one started.</summary>
    NotStarted,
}

/// <param name="UndoDetails">Extra details an executor learned while running, kept in the undo record.</param>
public sealed record ActionOutcome(
    PlannedAction Action,
    OutcomeStatus Status,
    string Message,
    IReadOnlyDictionary<string, string>? UndoDetails = null);

/// <summary>Carries out one kind of planned action. The Windows project provides the real ones.</summary>
public interface IActionExecutor
{
    bool CanRun(PlannedAction action);

    /// <summary>Does the change. Report failures as a Failed outcome where possible; exceptions are caught and logged too.</summary>
    Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken);
}

/// <summary>Questions and warnings the runner must put in front of the user. The app shows them as windows.</summary>
public interface ISafetyPrompts
{
    /// <summary>A restore point couldn't be made. Returns true to carry on without one, false to cancel.</summary>
    Task<bool> ContinueWithoutRestorePointAsync(RestorePointResult result, CancellationToken cancellationToken);

    /// <summary>The PC may be left without antivirus. Must be shown, with a way to open Windows Security.</summary>
    Task WarnAboutAntivirusAsync(AntivirusCheckResult result, CancellationToken cancellationToken);
}

public sealed record RunProgress(int Completed, int Total, string Current);

public sealed record RunReport(
    IReadOnlyList<ActionOutcome> Outcomes,
    RestorePointResult? RestorePoint,
    AntivirusCheckResult? Antivirus,
    bool CancelledBeforeStarting)
{
    public int Count(OutcomeStatus status) => Outcomes.Count(o => o.Status == status);
}

/// <summary>
/// Runs a confirmed plan and enforces the safety rules in code, whatever the UI does:
/// restore point first (rule 2), protected list re-checked right before every removal (rule 3),
/// Defender check after removing antivirus (rule 4), everything logged (rule 6), undo records kept
/// (rule 7) and nothing changed in dry-run mode (rule 9).
/// </summary>
public sealed class ActionRunner
{
    private readonly IAppLog _log;
    private readonly IRestorePointService _restorePoints;
    private readonly IAntivirusStatusService _antivirus;
    private readonly ISafetyPrompts _prompts;
    private readonly IReadOnlyList<IActionExecutor> _executors;
    private readonly IUndoJournal _undo;
    private readonly TimeProvider _time;

    public ActionRunner(
        IAppLog log,
        IRestorePointService restorePoints,
        IAntivirusStatusService antivirus,
        ISafetyPrompts prompts,
        IEnumerable<IActionExecutor> executors,
        IUndoJournal undo,
        TimeProvider? time = null)
    {
        _log = log;
        _restorePoints = restorePoints;
        _antivirus = antivirus;
        _prompts = prompts;
        _executors = executors.ToList();
        _undo = undo;
        _time = time ?? TimeProvider.System;
    }

    public async Task<RunReport> RunAsync(ConfirmedPlan confirmed, IProgress<RunProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmed);
        if (confirmed.HasRun)
        {
            throw new InvalidOperationException("This plan has already been run. Make a new plan and confirm it again.");
        }

        confirmed.HasRun = true;
        var plan = confirmed.Plan;
        var dryRun = confirmed.IsDryRun;
        var verb = dryRun ? "Would" : "Will";

        _log.Info($"Starting \"{plan.Title}\": {plan.Actions.Count} change(s) confirmed by the user.");
        foreach (var blocked in plan.Blocked)
        {
            _log.Warning($"Protected, not removed: {blocked.Action.Summary}. {blocked.Reason}");
        }

        // Safety rule 2: restore point before any removal.
        RestorePointResult? restorePoint = null;
        if (plan.NeedsRestorePoint)
        {
            if (dryRun)
            {
                _log.Info("Would create a System Restore point now.");
            }
            else
            {
                _log.Info("Creating a System Restore point.");
                restorePoint = await _restorePoints.CreateAsync(SafetyText.RestorePointDescription, cancellationToken).ConfigureAwait(false);

                if (restorePoint.IsCreated)
                {
                    _log.Info($"Restore point created. {restorePoint.TechnicalDetail}");
                }
                else
                {
                    _log.Warning($"Restore point not created ({restorePoint.Status}). {restorePoint.TechnicalDetail}");
                    var carryOn = await _prompts.ContinueWithoutRestorePointAsync(restorePoint, cancellationToken).ConfigureAwait(false);
                    if (!carryOn)
                    {
                        _log.Info("The user chose to cancel because there's no new restore point. Nothing was changed.");
                        var notStarted = plan.Actions.Select(a => new ActionOutcome(a, OutcomeStatus.NotStarted, "Cancelled before starting.")).ToList();
                        return new RunReport(notStarted, restorePoint, null, CancelledBeforeStarting: true);
                    }

                    _log.Info("The user chose to continue without a new restore point.");
                }
            }
        }

        var outcomes = new List<ActionOutcome>();
        var attemptedAntivirus = false;
        var removedAntivirus = new List<string>();
        for (var i = 0; i < plan.Actions.Count; i++)
        {
            var action = plan.Actions[i];
            if (cancellationToken.IsCancellationRequested)
            {
                _log.Info($"Cancelled. Not started: {action.Summary}.");
                outcomes.Add(new ActionOutcome(action, OutcomeStatus.NotStarted, "Cancelled before this was started."));
                continue;
            }

            progress?.Report(new RunProgress(i, plan.Actions.Count, action.Summary));
            var outcome = await RunOneAsync(action, dryRun, verb, cancellationToken).ConfigureAwait(false);
            outcomes.Add(outcome);

            if (action is RemoveAppAction { IsAntivirus: true } removal
                && outcome.Status is OutcomeStatus.Succeeded or OutcomeStatus.Failed or OutcomeStatus.DryRun)
            {
                // A failed removal may still have switched the antivirus off part-way, so check either way.
                attemptedAntivirus = true;
                if (outcome.Status == OutcomeStatus.Succeeded)
                {
                    removedAntivirus.Add(AntivirusBrand(removal));
                }
            }
        }

        progress?.Report(new RunProgress(plan.Actions.Count, plan.Actions.Count, "Finished"));

        // Safety rule 4: never leave the PC without antivirus without saying so.
        AntivirusCheckResult? antivirus = null;
        if (attemptedAntivirus)
        {
            // Only antivirus that was actually removed is ignored. One that's still installed
            // (a dry run, or a removal that failed) still counts as protection if Windows says it's on.
            antivirus = await CheckAntivirusAsync(removedAntivirus, cancellationToken).ConfigureAwait(false);
        }

        _log.Info($"Finished \"{plan.Title}\": {Summarise(outcomes)}.");
        return new RunReport(outcomes, restorePoint, antivirus, CancelledBeforeStarting: false);
    }

    private async Task<ActionOutcome> RunOneAsync(PlannedAction action, bool dryRun, string verb, CancellationToken cancellationToken)
    {
        // Safety rule 3: checked again here, right before the change, whatever built the plan.
        if (action.CheckProtection() is { IsProtected: true } protection)
        {
            _log.Warning($"Skipped \"{action.Summary}\": it's on the protected list. {protection.Reason}");
            return new ActionOutcome(action, OutcomeStatus.Skipped, protection.Reason!);
        }

        var executor = _executors.FirstOrDefault(e => e.CanRun(action));
        if (executor is null)
        {
            _log.Error($"No way to carry out \"{action.Summary}\" ({action.GetType().Name}). Skipped.");
            return new ActionOutcome(action, OutcomeStatus.Failed, "AM Speed Up doesn't know how to do this.");
        }

        if (dryRun)
        {
            _log.Info($"{verb} {char.ToLowerInvariant(action.Summary[0])}{action.Summary[1..]}.");
            return new ActionOutcome(action, OutcomeStatus.DryRun, "Test mode: nothing was changed.");
        }

        _log.Info($"Starting: {action.Summary}.");
        ActionOutcome outcome;
        try
        {
            outcome = await executor.RunAsync(action, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log.Warning($"Stopped part-way through: {action.Summary}.");
            return new ActionOutcome(action, OutcomeStatus.Failed, "Stopped before it finished.");
        }
        catch (Exception ex)
        {
            _log.Error($"Failed: {action.Summary}.", ex);
            return new ActionOutcome(action, OutcomeStatus.Failed, "Something went wrong. The details are in the log.");
        }

        switch (outcome.Status)
        {
            case OutcomeStatus.Succeeded:
                _log.Info($"Done: {action.Summary}. {outcome.Message}".TrimEnd());
                RecordUndo(action, outcome);
                break;
            case OutcomeStatus.Failed:
                _log.Error($"Failed: {action.Summary}. {outcome.Message}".TrimEnd());
                break;
            default:
                _log.Warning($"{outcome.Status}: {action.Summary}. {outcome.Message}".TrimEnd());
                break;
        }

        return outcome;
    }

    private void RecordUndo(PlannedAction action, ActionOutcome outcome)
    {
        var entry = action.CreateUndoEntry(_time.GetUtcNow());
        if (entry is null)
        {
            return;
        }

        if (outcome.UndoDetails is { Count: > 0 } details)
        {
            var data = new Dictionary<string, string>(entry.Data);
            foreach (var (key, value) in details)
            {
                data[key] = value;
            }

            entry = entry with { Data = data };
        }

        try
        {
            _undo.Add(entry);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The change itself worked, so don't report it as failed. Make sure it's in the log instead.
            _log.Error($"Couldn't save the undo record for \"{action.Summary}\". The change was still made.", ex);
        }
    }

    private async Task<AntivirusCheckResult> CheckAntivirusAsync(IReadOnlyList<string> removed, CancellationToken cancellationToken)
    {
        _log.Info("Antivirus was removed, so checking that Microsoft Defender is protecting the PC.");
        AntivirusCheckResult result;
        try
        {
            var snapshot = await _antivirus.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot.DefenderError is not null)
            {
                _log.Warning($"Couldn't read Microsoft Defender's status: {snapshot.DefenderError}");
            }

            result = AntivirusCheck.Evaluate(snapshot, removed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _log.Error("Couldn't check the antivirus status.", ex);
            result = new AntivirusCheckResult(AntivirusVerdict.Unknown);
        }

        _log.Write(result.NeedsWarning ? LogLevel.Warning : LogLevel.Info, $"Antivirus check: {SafetyText.AntivirusSummary(result)}");
        if (result.NeedsWarning)
        {
            await _prompts.WarnAboutAntivirusAsync(result, cancellationToken).ConfigureAwait(false);
        }

        return result;
    }

    private static string AntivirusBrand(RemoveAppAction removal) => removal.VendorName;

    private static string Summarise(IReadOnlyList<ActionOutcome> outcomes) =>
        string.Join(", ", outcomes.GroupBy(o => o.Status).OrderBy(g => g.Key).Select(g => $"{g.Count()} {g.Key.ToString().ToLowerInvariant()}"));
}
