using System.Globalization;
using System.Text.Json;

namespace AMSpeedUp.Core.Safety;

/// <summary>
/// Reads the JSON printed by the Windows restore point and antivirus scripts. Kept here, away from
/// Windows, so it can be tested with sample output on any machine.
/// </summary>
public static class WindowsResultParsers
{
    public static RestorePointAttempt ParseRestorePointAttempt(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        return new RestorePointAttempt
        {
            CommandSucceeded = GetBool(root, "commandSucceeded") ?? false,
            Error = GetString(root, "error"),
            Warnings = GetStrings(root, "warnings"),
            DisabledByPolicy = GetBool(root, "policyDisabled") ?? false,
            LatestSequenceBefore = GetPoint(root, "before").Sequence,
            LatestCreatedBefore = GetPoint(root, "before").Created,
            LatestSequenceAfter = GetPoint(root, "after").Sequence,
            LatestCreatedAfter = GetPoint(root, "after").Created,
        };
    }

    public static AntivirusSnapshot ParseAntivirusSnapshot(string json)
    {
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        DefenderStatus? defender = null;
        if (root.TryGetProperty("defender", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            defender = new DefenderStatus(
                GetBool(d, "antivirusEnabled") ?? false,
                GetBool(d, "realTimeProtectionEnabled") ?? false,
                GetString(d, "amRunningMode"));
        }

        List<RegisteredAntivirus>? products = null;
        if (root.TryGetProperty("products", out var p))
        {
            products = [];
            // ConvertTo-Json in Windows PowerShell turns a one-item list into a single object.
            var items = p.ValueKind switch
            {
                JsonValueKind.Array => p.EnumerateArray().ToList(),
                JsonValueKind.Object => [p],
                _ => [],
            };

            foreach (var item in items)
            {
                var name = GetString(item, "name");
                if (name is not null && item.TryGetProperty("state", out var state) && state.TryGetInt32(out var value))
                {
                    products.Add(new RegisteredAntivirus(name, value));
                }
            }
        }

        var productsError = GetString(root, "productsError");
        return new AntivirusSnapshot(defender, GetString(root, "defenderError"), productsError is null ? products : null, productsError);
    }

    private static (long? Sequence, DateTimeOffset? Created) GetPoint(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var point) || point.ValueKind != JsonValueKind.Object)
        {
            return (null, null);
        }

        long? sequence = point.TryGetProperty("sequence", out var s) && s.TryGetInt64(out var n) ? n : null;
        DateTimeOffset? created = DateTimeOffset.TryParse(GetString(point, "created"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var when)
            ? when
            : null;
        return (sequence, created);
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? value.GetBoolean()
            : null;

    private static IReadOnlyList<string> GetStrings(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            return [];
        }

        return value.ValueKind switch
        {
            JsonValueKind.Array => value.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!).ToList(),
            JsonValueKind.String => [value.GetString()!],
            _ => [],
        };
    }
}
