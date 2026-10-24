using System.Text.Json;

namespace AMSpeedUp.Core.Inventory;

/// <summary>
/// Reads the Store app list printed by the Windows scan script (Get-AppxPackage and
/// Get-AppxProvisionedPackage) and turns it into one <see cref="InstalledApp"/> per app.
/// </summary>
public static class StorePackages
{
    public static InventoryResult Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        var problems = new List<string>();

        if (Text(root, "packagesError") is { } packagesError)
        {
            problems.Add("Couldn't list Store apps: " + packagesError);
        }

        if (Text(root, "provisionedError") is { } provisionedError)
        {
            problems.Add("Couldn't list Store apps kept for new users: " + provisionedError);
        }

        var byName = new Dictionary<string, List<JsonElement>>(StringComparer.OrdinalIgnoreCase);
        foreach (var package in Items(root, "packages"))
        {
            if (Text(package, "name") is not { } name || Flag(package, "isResource"))
            {
                continue;
            }

            if (!byName.TryGetValue(name, out var list))
            {
                byName[name] = list = [];
            }

            list.Add(package);
        }

        var provisioned = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in Items(root, "provisioned"))
        {
            if (Text(item, "name") is { } name && Text(item, "packageName") is { } packageName)
            {
                provisioned[name] = packageName;
            }
        }

        var apps = new List<InstalledApp>();
        foreach (var (name, packages) in byName)
        {
            var first = packages[0];
            apps.Add(new InstalledApp
            {
                Kind = AppKind.StoreApp,
                Id = name,
                DisplayName = StoreAppNames.Friendly(name),
                Publisher = Text(first, "publisher"),
                PackageFamilyName = Text(first, "familyName"),
                PackageFullNames = packages.Select(p => Text(p, "fullName")).OfType<string>().Distinct().ToList(),
                SignatureKind = packages.Select(p => ParseSignature(Text(p, "signatureKind"))).Max(),
                IsFramework = packages.Any(p => Flag(p, "isFramework")),
                IsNonRemovable = packages.Any(p => Flag(p, "nonRemovable")),
                ProvisionedPackageName = provisioned.GetValueOrDefault(name),
            });
        }

        // Apps Windows will install for new user accounts but this user doesn't have.
        foreach (var (name, packageName) in provisioned.Where(p => !byName.ContainsKey(p.Key)))
        {
            apps.Add(new InstalledApp
            {
                Kind = AppKind.StoreApp,
                Id = name,
                DisplayName = StoreAppNames.Friendly(name),
                PackageFamilyName = FamilyNameFromFullName(packageName),
                ProvisionedPackageName = packageName,
            });
        }

        return new InventoryResult(apps, problems);
    }

    /// <summary>"Name_Version_Arch_ResourceId_PublisherId" becomes "Name_PublisherId".</summary>
    public static string? FamilyNameFromFullName(string fullName)
    {
        var parts = fullName.Split('_');
        return parts.Length >= 5 ? $"{parts[0]}_{parts[^1]}" : null;
    }

    // System is the highest value, so if any copy is a system package the app counts as one.
    private static PackageSignatureKind ParseSignature(string? text) =>
        Enum.TryParse<PackageSignatureKind>(text, ignoreCase: true, out var kind) ? kind : PackageSignatureKind.Unknown;

    private static IEnumerable<JsonElement> Items(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value))
        {
            return [];
        }

        // Windows PowerShell's ConvertTo-Json writes a one-item list as a single object.
        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().ToList(),
            JsonValueKind.Object => [value],
            _ => [],
        };
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.True;
}
