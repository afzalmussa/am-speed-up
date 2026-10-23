namespace AMSpeedUp.Core.Inventory;

/// <param name="Problems">Parts of the scan that didn't work, for the log. The rest of the results are still usable.</param>
public sealed record InventoryResult(IReadOnlyList<InstalledApp> Apps, IReadOnlyList<string> Problems);

/// <summary>Finds the Store apps and desktop programs installed on the PC. Read-only.</summary>
public interface IAppInventory
{
    Task<InventoryResult> ScanAsync(CancellationToken cancellationToken = default);
}
