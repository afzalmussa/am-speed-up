using System.Text.RegularExpressions;

namespace AMSpeedUp.Core.Inventory;

/// <summary>One program's details as read from an Uninstall registry key. Gathered by the Windows project.</summary>
public sealed record UninstallEntry
{
    public required string KeyName { get; init; }

    /// <summary>Full key path including the hive, for example HKLM\SOFTWARE\Microsoft\...\Uninstall\{GUID}.</summary>
    public required string KeyPath { get; init; }

    public string? DisplayName { get; init; }

    public string? Publisher { get; init; }

    public string? DisplayVersion { get; init; }

    public string? UninstallString { get; init; }

    public string? QuietUninstallString { get; init; }

    public int? SystemComponent { get; init; }

    public int? WindowsInstaller { get; init; }

    public string? ParentKeyName { get; init; }

    public string? ReleaseType { get; init; }
}

public static partial class UninstallEntries
{
    /// <summary>
    /// Turns raw registry entries into the programs a person would see in "Installed apps".
    /// Updates, patches and entries with no way to uninstall are left out. When the same program
    /// is listed more than once (for example in both the 64-bit and 32-bit lists), it appears once.
    /// </summary>
    public static IReadOnlyList<InstalledApp> ToInstalledApps(IEnumerable<UninstallEntry> entries)
    {
        var apps = new List<InstalledApp>();
        var seen = new HashSet<(string, string)>();

        foreach (var entry in entries)
        {
            var name = entry.DisplayName?.Trim();
            if (string.IsNullOrEmpty(name))
            {
                continue;
            }

            // Updates and patches belong to another program.
            if (!string.IsNullOrWhiteSpace(entry.ParentKeyName)
                || entry.ReleaseType is { } release && (release.Contains("Update", StringComparison.OrdinalIgnoreCase) || release.Contains("Hotfix", StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var productCode = entry.WindowsInstaller == 1 && ProductCode().IsMatch(entry.KeyName) ? entry.KeyName.ToUpperInvariant() : null;
            if (productCode is null && string.IsNullOrWhiteSpace(entry.UninstallString) && string.IsNullOrWhiteSpace(entry.QuietUninstallString))
            {
                continue;
            }

            if (!seen.Add((name.ToLowerInvariant(), (entry.Publisher ?? "").Trim().ToLowerInvariant())))
            {
                continue;
            }

            apps.Add(new InstalledApp
            {
                Kind = AppKind.DesktopProgram,
                Id = entry.KeyPath,
                DisplayName = name,
                Publisher = entry.Publisher?.Trim(),
                DisplayVersion = entry.DisplayVersion?.Trim(),
                RegistryKeyPath = entry.KeyPath,
                MsiProductCode = productCode,
                UninstallString = entry.UninstallString?.Trim(),
                QuietUninstallString = entry.QuietUninstallString?.Trim(),
                IsSystemComponent = entry.SystemComponent == 1,
            });
        }

        return apps;
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}$")]
    internal static partial Regex ProductCode();
}
