using System.Text.Json;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;

namespace AMSpeedUp.Windows.Inventory;

/// <summary>Finds installed Store apps (for this user and for new users) and desktop programs.</summary>
public sealed class WindowsAppInventory(IPowerShellRunner powerShell, IAppLog log) : IAppInventory
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(3);

    private const string StoreScript = """
        $result = [ordered]@{ packages = @(); packagesError = $null; provisioned = @(); provisionedError = $null }
        try {
            $result.packages = @(Get-AppxPackage -ErrorAction Stop | ForEach-Object {
                [ordered]@{
                    name = [string]$_.Name
                    fullName = [string]$_.PackageFullName
                    familyName = [string]$_.PackageFamilyName
                    publisher = [string]$_.Publisher
                    signatureKind = [string]$_.SignatureKind
                    isFramework = [bool]$_.IsFramework
                    nonRemovable = [bool]$_.NonRemovable
                    isResource = [bool]$_.IsResourcePackage
                }
            })
        } catch { $result.packagesError = $_.Exception.Message }
        try {
            $result.provisioned = @(Get-AppxProvisionedPackage -Online -ErrorAction Stop | ForEach-Object {
                [ordered]@{ name = [string]$_.DisplayName; packageName = [string]$_.PackageName }
            })
        } catch { $result.provisionedError = $_.Exception.Message }
        $result | ConvertTo-Json -Compress -Depth 4
        """;

    public async Task<InventoryResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        log.Info("Scanning installed apps.");
        var storeTask = ScanStoreAsync(cancellationToken);
        var desktopTask = Task.Run(UninstallRegistry.ReadAll, cancellationToken);

        var store = await storeTask.ConfigureAwait(false);
        var (entries, registryProblems) = await desktopTask.ConfigureAwait(false);
        var desktop = UninstallEntries.ToInstalledApps(entries);

        var problems = store.Problems.Concat(registryProblems).ToList();
        foreach (var problem in problems)
        {
            log.Warning("Scan problem: " + problem);
        }

        log.Info($"Found {store.Apps.Count} Store app(s) and {desktop.Count} desktop program(s).");
        return new InventoryResult([.. store.Apps, .. desktop], problems);
    }

    private async Task<InventoryResult> ScanStoreAsync(CancellationToken cancellationToken)
    {
        var run = await powerShell.RunAsync(StoreScript, "Listing Store apps", Timeout, cancellationToken).ConfigureAwait(false);
        var json = PowerShellOutput.LastJsonLine(run.Output);
        if (json is null)
        {
            return new InventoryResult([], [run.TimedOut ? "Listing Store apps took too long." : $"Listing Store apps failed. {run.Errors}"]);
        }

        try
        {
            return StorePackages.Parse(json);
        }
        catch (JsonException ex)
        {
            return new InventoryResult([], ["Couldn't read the Store app list: " + ex.Message]);
        }
    }
}
