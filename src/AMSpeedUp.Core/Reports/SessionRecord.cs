using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;

namespace AMSpeedUp.Core.Reports;

/// <summary>A figure as it was first seen this session and as it was seen most recently.</summary>
public sealed record Measurement<T>(T Before, T After);

public sealed record SessionChange(DateTimeOffset When, string Summary);

/// <summary>
/// What happened while the app was open, for the before and after report. Each tab records what
/// it measures. Nothing can be changed in a tab without measuring it first, so the first figure
/// for each one is always from before any change there.
/// </summary>
public sealed class SessionRecord(TimeProvider? time = null)
{
    private readonly object _gate = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly List<SessionChange> _changes = [];
    private readonly List<SessionChange> _testModeChanges = [];
    private Measurement<DriveSpace>? _freeSpace;
    private Measurement<int>? _startupOn;
    private Measurement<int>? _appsFound;

    public DateTimeOffset Started { get; } = (time ?? TimeProvider.System).GetUtcNow();

    public Measurement<DriveSpace>? FreeSpace
    {
        get { lock (_gate) { return _freeSpace; } }
    }

    public Measurement<int>? StartupOn
    {
        get { lock (_gate) { return _startupOn; } }
    }

    public Measurement<int>? AppsFound
    {
        get { lock (_gate) { return _appsFound; } }
    }

    public IReadOnlyList<SessionChange> Changes
    {
        get { lock (_gate) { return _changes.ToList(); } }
    }

    public IReadOnlyList<SessionChange> TestModeChanges
    {
        get { lock (_gate) { return _testModeChanges.ToList(); } }
    }

    public void RecordFreeSpace(DriveSpace? space)
    {
        if (space is not null)
        {
            lock (_gate) { _freeSpace = Next(_freeSpace, space); }
        }
    }

    public void RecordStartupOn(int count)
    {
        lock (_gate) { _startupOn = Next(_startupOn, count); }
    }

    public void RecordAppsFound(int count)
    {
        lock (_gate) { _appsFound = Next(_appsFound, count); }
    }

    /// <summary>Keeps what a finished run changed. Hook it to <see cref="ActionRunner.Finished"/>.</summary>
    public void RecordRun(RunReport report)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            foreach (var outcome in report.Outcomes)
            {
                switch (outcome.Status)
                {
                    case OutcomeStatus.Succeeded:
                        _changes.Add(new SessionChange(now, outcome.Action.Summary));
                        break;
                    case OutcomeStatus.DryRun:
                        _testModeChanges.Add(new SessionChange(now, outcome.Action.Summary));
                        break;
                }
            }
        }
    }

    private static Measurement<T> Next<T>(Measurement<T>? current, T value) =>
        current is null ? new Measurement<T>(value, value) : current with { After = value };
}
