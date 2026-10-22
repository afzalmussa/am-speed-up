using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Undo;

namespace AMSpeedUp.Tests.Fakes;

/// <summary>Shared record of calls, so tests can check the order things happened in.</summary>
public sealed class CallLog
{
    public List<string> Calls { get; } = [];

    public void Add(string call)
    {
        lock (Calls)
        {
            Calls.Add(call);
        }
    }
}

public sealed class MemoryLog : IAppLog
{
    public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = [];

    public string LogDirectory => "/logs";

    public string CurrentLogFile => "/logs/today.log";

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        lock (Entries)
        {
            Entries.Add((level, message, exception));
        }
    }

    public bool Has(LogLevel level, string fragment) =>
        Entries.Any(e => e.Level == level && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}

public sealed class FakeRestorePoints(CallLog calls) : IRestorePointService
{
    public RestorePointResult Result { get; set; } = new(RestorePointStatus.Created, DateTimeOffset.UnixEpoch);

    public int Created { get; private set; }

    public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default)
    {
        calls.Add("restore point");
        Created++;
        return Task.FromResult(Result);
    }
}

public sealed class FakeAntivirus(CallLog calls) : IAntivirusStatusService
{
    public AntivirusSnapshot Snapshot { get; set; } = new(new DefenderStatus(true, true, "Normal"), null, [], null);

    public Exception? Throws { get; set; }

    public Task<AntivirusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        calls.Add("antivirus check");
        if (Throws is not null)
        {
            throw Throws;
        }

        return Task.FromResult(Snapshot);
    }
}

public sealed class FakePrompts(CallLog calls) : ISafetyPrompts
{
    public bool ContinueWithoutRestorePoint { get; set; }

    public List<RestorePointResult> RestorePointQuestions { get; } = [];

    public List<AntivirusCheckResult> AntivirusWarnings { get; } = [];

    public Task<bool> ContinueWithoutRestorePointAsync(RestorePointResult result, CancellationToken cancellationToken)
    {
        calls.Add("ask about restore point");
        RestorePointQuestions.Add(result);
        return Task.FromResult(ContinueWithoutRestorePoint);
    }

    public Task WarnAboutAntivirusAsync(AntivirusCheckResult result, CancellationToken cancellationToken)
    {
        calls.Add("antivirus warning");
        AntivirusWarnings.Add(result);
        return Task.CompletedTask;
    }
}

/// <summary>Runs RemoveAppActions, or whatever <see cref="Handles"/> allows, by recording them.</summary>
public sealed class FakeExecutor(CallLog calls) : IActionExecutor
{
    public Func<PlannedAction, bool> Handles { get; set; } = _ => true;

    public Func<PlannedAction, ActionOutcome>? Behaviour { get; set; }

    public List<PlannedAction> Ran { get; } = [];

    public bool CanRun(PlannedAction action) => Handles(action);

    public Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        calls.Add("run: " + action.Summary);
        Ran.Add(action);
        return Task.FromResult(Behaviour?.Invoke(action) ?? new ActionOutcome(action, OutcomeStatus.Succeeded, ""));
    }
}

public sealed class MemoryUndoJournal : IUndoJournal
{
    public List<UndoEntry> Entries { get; } = [];

    public Exception? ThrowOnAdd { get; set; }

    public IReadOnlyList<UndoEntry> GetAll() => Entries.ToList();

    public void Add(UndoEntry entry)
    {
        if (ThrowOnAdd is not null)
        {
            throw ThrowOnAdd;
        }

        Entries.Add(entry);
    }

    public bool MarkUndone(Guid id, DateTimeOffset when)
    {
        var index = Entries.FindIndex(e => e.Id == id);
        if (index < 0)
        {
            return false;
        }

        Entries[index] = Entries[index] with { UndoneAt = when };
        return true;
    }
}

/// <summary>A planned action that isn't a removal, for checking non-removal paths.</summary>
public sealed record FakeStartupAction(string Name) : PlannedAction
{
    public override string Summary => $"Turn off {Name} at startup";

    public override UndoSupport Undo => UndoSupport.InApp;
}

public sealed record FakeCleanupAction(string Name) : PlannedAction
{
    public override string Summary => $"Delete {Name}";

    public override UndoSupport Undo => UndoSupport.CannotUndo;
}

public static class Apps
{
    public static InstalledApp Store(string id, string? name = null, PackageSignatureKind signature = PackageSignatureKind.Store) => new()
    {
        Kind = AppKind.StoreApp,
        Id = id,
        DisplayName = name ?? id,
        PackageFamilyName = id + "_8wekyb3d8bbwe",
        SignatureKind = signature,
    };

    public static InstalledApp Desktop(string name, string? publisher = null) => new()
    {
        Kind = AppKind.DesktopProgram,
        Id = "uninstall-" + name,
        DisplayName = name,
        Publisher = publisher,
    };

    public static PresetEntry Entry(
        AppKind kind,
        string match,
        Recommendation recommendation = Recommendation.Remove,
        string? name = null,
        bool antivirus = false,
        VendorRemovalTool tool = VendorRemovalTool.None,
        string? publisher = null) => new()
    {
        Kind = kind,
        Match = match,
        Name = name ?? match,
        Description = "Test entry",
        Recommendation = recommendation,
        IsAntivirus = antivirus || tool != VendorRemovalTool.None,
        VendorTool = tool,
        Publisher = publisher,
    };

    public static Preset Preset(string id, PresetTier tier, params PresetEntry[] entries) => new()
    {
        Id = id,
        Name = id,
        Description = "Test preset",
        Tier = tier,
        Entries = entries,
        Source = id + ".json",
    };
}
