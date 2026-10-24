using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.Core.VendorTools;

namespace AMSpeedUp.UI.Tests;

internal sealed class FakeInventory : IAppInventory
{
    public List<InstalledApp> Apps { get; } = [];

    public Exception? Throws { get; set; }

    public int Scans { get; private set; }

    public Task<InventoryResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        Scans++;
        return Throws is not null ? Task.FromException<InventoryResult>(Throws) : Task.FromResult(new InventoryResult(Apps.ToList(), []));
    }
}

internal sealed class FakeRestorePoints : IRestorePointService
{
    public int Created { get; private set; }

    public Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default)
    {
        Created++;
        return Task.FromResult(new RestorePointResult(RestorePointStatus.Created, DateTimeOffset.Now));
    }
}

internal sealed class FakeAntivirus : IAntivirusStatusService
{
    public AntivirusSnapshot Snapshot { get; set; } = new(new DefenderStatus(true, true, "Normal"), null, [], null);

    public Task<AntivirusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) => Task.FromResult(Snapshot);
}

internal sealed class AlwaysContinuePrompts : ISafetyPrompts
{
    public Task<bool> ContinueWithoutRestorePointAsync(RestorePointResult result, CancellationToken cancellationToken) => Task.FromResult(true);

    public Task WarnAboutAntivirusAsync(AntivirusCheckResult result, CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>Removes anything instantly, and removes it from the fake inventory too.</summary>
internal sealed class FakeRemover(FakeInventory inventory) : IActionExecutor
{
    public List<RemoveAppAction> Removed { get; } = [];

    public bool CanRun(PlannedAction action) => action is RemoveAppAction;

    public Task<ActionOutcome> RunAsync(PlannedAction action, CancellationToken cancellationToken)
    {
        var removal = (RemoveAppAction)action;
        Removed.Add(removal);
        inventory.Apps.RemoveAll(a => a.Id == removal.App.Id);
        return Task.FromResult(new ActionOutcome(action, OutcomeStatus.Succeeded, ""));
    }
}

internal sealed class MemoryUndo : IUndoJournal
{
    public List<UndoEntry> Entries { get; } = [];

    public IReadOnlyList<UndoEntry> GetAll() => Entries.ToList();

    public void Add(UndoEntry entry) => Entries.Add(entry);

    public bool MarkUndone(Guid id, DateTimeOffset when) => false;
}

internal sealed class FakeAfterRestart : IAntivirusCheckAfterRestart
{
    public int Scheduled { get; private set; }

    public int Cancelled { get; private set; }

    public void Schedule() => Scheduled++;

    public void Cancel() => Cancelled++;
}
