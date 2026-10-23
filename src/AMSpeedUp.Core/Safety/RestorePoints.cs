namespace AMSpeedUp.Core.Safety;

public enum RestorePointStatus
{
    Created,

    /// <summary>System Restore is switched off on this PC, or blocked by a policy.</summary>
    TurnedOff,

    /// <summary>Windows only allows one new restore point a day by default, and one was made recently.</summary>
    TooSoon,

    Failed,
}

/// <param name="When">For Created, when it was made. For TooSoon, when the existing recent one was made.</param>
/// <param name="TechnicalDetail">Exact error text, for the log only.</param>
public sealed record RestorePointResult(RestorePointStatus Status, DateTimeOffset? When = null, string? TechnicalDetail = null)
{
    public bool IsCreated => Status == RestorePointStatus.Created;
}

public interface IRestorePointService
{
    Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default);
}

/// <summary>What Windows reported while trying to make a restore point. Gathered by the Windows implementation.</summary>
public sealed record RestorePointAttempt
{
    public bool CommandSucceeded { get; init; }

    public string? Error { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool DisabledByPolicy { get; init; }

    public long? LatestSequenceBefore { get; init; }

    public DateTimeOffset? LatestCreatedBefore { get; init; }

    public long? LatestSequenceAfter { get; init; }

    public DateTimeOffset? LatestCreatedAfter { get; init; }
}

public static class RestorePointClassifier
{
    /// <summary>Windows' default gap between automatic restore points (the "1440 minutes" in its warning).</summary>
    public static readonly TimeSpan FrequencyLimit = TimeSpan.FromHours(24);

    private static readonly string[] TurnedOffSignals =
    [
        "disabled",
        "turned off",
        "0x80070422", // the service is disabled
        "1058",       // ERROR_SERVICE_DISABLED
        "service cannot be started",
    ];

    private static readonly string[] TooSoonSignals =
    [
        "1440 minutes",
        "already been created within the past",
    ];

    /// <summary>
    /// Works out what actually happened. Windows can report success without making a restore point
    /// (when one was made in the last 24 hours), so success is only trusted if a new point appears.
    /// </summary>
    public static RestorePointResult Classify(RestorePointAttempt attempt, DateTimeOffset now)
    {
        var detail = Describe(attempt);

        if (attempt.DisabledByPolicy)
        {
            return new RestorePointResult(RestorePointStatus.TurnedOff, TechnicalDetail: "System Restore is turned off by a policy. " + detail);
        }

        if (IsNewPoint(attempt))
        {
            return new RestorePointResult(RestorePointStatus.Created, attempt.LatestCreatedAfter ?? now, detail);
        }

        var everything = string.Join(" ", new[] { attempt.Error }.Concat(attempt.Warnings).Where(s => s is not null));

        if (TooSoonSignals.Any(s => everything.Contains(s, StringComparison.OrdinalIgnoreCase)))
        {
            return new RestorePointResult(RestorePointStatus.TooSoon, attempt.LatestCreatedBefore, detail);
        }

        if (!attempt.CommandSucceeded && TurnedOffSignals.Any(s => everything.Contains(s, StringComparison.OrdinalIgnoreCase)))
        {
            return new RestorePointResult(RestorePointStatus.TurnedOff, TechnicalDetail: detail);
        }

        if (attempt.CommandSucceeded && attempt.LatestCreatedBefore is { } latest && now - latest < FrequencyLimit)
        {
            // Windows said "done" but quietly skipped it because of the daily limit.
            return new RestorePointResult(RestorePointStatus.TooSoon, latest, detail);
        }

        return new RestorePointResult(RestorePointStatus.Failed, TechnicalDetail: detail);
    }

    private static bool IsNewPoint(RestorePointAttempt attempt)
    {
        if (attempt.LatestSequenceAfter is not { } after)
        {
            return false;
        }

        return attempt.LatestSequenceBefore is not { } before || after > before;
    }

    private static string Describe(RestorePointAttempt a) =>
        $"Command succeeded: {a.CommandSucceeded}. Error: {a.Error ?? "none"}. " +
        $"Warnings: {(a.Warnings.Count == 0 ? "none" : string.Join(" | ", a.Warnings))}. " +
        $"Latest before: #{a.LatestSequenceBefore?.ToString() ?? "none"} at {a.LatestCreatedBefore?.ToString("u") ?? "n/a"}. " +
        $"Latest after: #{a.LatestSequenceAfter?.ToString() ?? "none"} at {a.LatestCreatedAfter?.ToString("u") ?? "n/a"}.";
}
