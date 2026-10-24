using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Core.Actions;

/// <summary>Turns a finished run into a few short lines for the user.</summary>
public static class RunSummary
{
    public static IReadOnlyList<string> Describe(RunReport report)
    {
        if (report.CancelledBeforeStarting)
        {
            return ["Nothing was changed."];
        }

        var lines = new List<string>();
        var dryRun = report.Count(OutcomeStatus.DryRun);
        var done = report.Count(OutcomeStatus.Succeeded);

        if (dryRun > 0)
        {
            lines.Add($"Test mode: {Things(dryRun)} would have been changed. Nothing on this PC was changed.");
        }
        else if (done > 0)
        {
            lines.Add($"Done: {Things(done)} changed successfully.");
        }

        foreach (var outcome in report.Outcomes.Where(o => o.Status == OutcomeStatus.Failed))
        {
            lines.Add($"Not done: {Name(outcome)}. {outcome.Message}".TrimEnd());
        }

        foreach (var outcome in report.Outcomes.Where(o => o.Status == OutcomeStatus.Skipped))
        {
            lines.Add($"Skipped: {Name(outcome)}. {outcome.Message}".TrimEnd());
        }

        var notStarted = report.Count(OutcomeStatus.NotStarted);
        if (notStarted > 0)
        {
            lines.Add($"Stopped before {Things(notStarted)} could be changed.");
        }

        foreach (var outcome in report.Outcomes.Where(o => o.Status == OutcomeStatus.Succeeded && !string.IsNullOrWhiteSpace(o.Message)))
        {
            lines.Add($"{Name(outcome)}: {outcome.Message}");
        }

        if (report.RestorePoint is { } restorePoint)
        {
            lines.Add(restorePoint.Status switch
            {
                RestorePointStatus.Created when restorePoint.When is { } when => $"A restore point was saved at {SafetyText.FormatWhen(when)}.",
                RestorePointStatus.Created => "A restore point was saved first.",
                RestorePointStatus.TooSoon when restorePoint.When is { } when => $"No new restore point was saved. The one from {SafetyText.FormatWhen(when)} can be used instead.",
                _ => "No restore point was saved.",
            });
        }

        if (report.Antivirus is { } antivirus)
        {
            lines.Add(SafetyText.AntivirusSummary(antivirus));
        }

        return lines;
    }

    private static string Name(ActionOutcome outcome) =>
        outcome.Action is RemoveAppAction removal ? removal.App.DisplayName : outcome.Action.Summary;

    private static string Things(int count) => count == 1 ? "1 thing" : $"{count} things";
}
