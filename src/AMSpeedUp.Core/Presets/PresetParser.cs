using System.Text.Json;
using System.Text.RegularExpressions;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Matching;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Core.Presets;

/// <param name="Preset">The usable preset, or null if the file couldn't be used at all.</param>
/// <param name="Problems">Everything that was wrong or skipped, written for the log.</param>
public sealed record PresetParseResult(Preset? Preset, IReadOnlyList<string> Problems);

/// <summary>
/// Reads a preset JSON file. Bad entries are skipped with a logged reason rather than failing the
/// whole file, and any entry that could reach the protected list is always skipped (safety rule 3).
/// </summary>
public static partial class PresetParser
{
    /// <summary>Patterns need at least this many real characters, so "*" or "Mic*" can't sweep up half the PC.</summary>
    public const int MinimumLiteralLength = 4;

    private static readonly HashSet<string> PresetProperties = ["$schema", "id", "name", "description", "tier", "items"];

    private static readonly HashSet<string> EntryProperties =
        ["type", "match", "publisher", "name", "description", "recommendation", "antivirus", "vendorTool"];

    public static PresetParseResult Parse(string json, string source)
    {
        var problems = new List<string>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            });
        }
        catch (JsonException ex)
        {
            problems.Add($"{source}: not valid JSON ({ex.Message}). The whole preset was skipped.");
            return new PresetParseResult(null, problems);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add($"{source}: the file must contain a JSON object. The whole preset was skipped.");
                return new PresetParseResult(null, problems);
            }

            WarnAboutUnknownProperties(root, PresetProperties, source, problems);

            var id = ReadString(root, "id");
            var name = ReadString(root, "name");
            var description = ReadString(root, "description");
            var tierText = ReadString(root, "tier");

            var fatal = false;
            if (id is null || !IdFormat().IsMatch(id))
            {
                problems.Add($"{source}: \"id\" is missing or isn't lowercase letters, numbers and dashes.");
                fatal = true;
            }

            if (name is null)
            {
                problems.Add($"{source}: \"name\" is missing.");
                fatal = true;
            }

            if (description is null)
            {
                problems.Add($"{source}: \"description\" is missing.");
                fatal = true;
            }

            PresetTier tier = default;
            if (!TryParseTier(tierText, out tier))
            {
                // Never guess the tier: a typo shouldn't quietly make a Pro preset free or the other way round.
                problems.Add($"{source}: \"tier\" must be \"free\" or \"pro\".");
                fatal = true;
            }

            if (!root.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{source}: \"items\" must be a list.");
                fatal = true;
            }

            if (fatal)
            {
                problems.Add($"{source}: the whole preset was skipped.");
                return new PresetParseResult(null, problems);
            }

            var entries = new List<PresetEntry>();
            var seen = new HashSet<(AppKind, string)>();
            var index = 0;
            foreach (var item in items.EnumerateArray())
            {
                index++;
                var entry = ParseEntry(item, $"{source} item {index}", problems);
                if (entry is null)
                {
                    continue;
                }

                if (!seen.Add((entry.Kind, entry.Match.ToLowerInvariant())))
                {
                    problems.Add($"{source} item {index} ({entry.Name}): duplicate of an earlier item. Skipped.");
                    continue;
                }

                entries.Add(entry);
            }

            var preset = new Preset
            {
                Id = id!,
                Name = name!,
                Description = description!,
                Tier = tier,
                Entries = entries,
                Source = source,
            };
            return new PresetParseResult(preset, problems);
        }
    }

    private static PresetEntry? ParseEntry(JsonElement item, string where, List<string> problems)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"{where}: must be a JSON object. Skipped.");
            return null;
        }

        WarnAboutUnknownProperties(item, EntryProperties, where, problems);

        var name = ReadString(item, "name");
        var label = name is null ? where : $"{where} ({name})";
        var typeText = ReadString(item, "type");
        var match = ReadString(item, "match");
        var description = ReadString(item, "description");
        var recommendationText = ReadString(item, "recommendation");
        var publisher = ReadString(item, "publisher");
        var vendorToolText = ReadString(item, "vendorTool");

        var errors = new List<string>();
        AppKind kind = default;
        switch (typeText?.ToLowerInvariant())
        {
            case "store":
                kind = AppKind.StoreApp;
                break;
            case "desktop":
                kind = AppKind.DesktopProgram;
                break;
            default:
                errors.Add("\"type\" must be \"store\" or \"desktop\"");
                break;
        }

        if (match is null)
        {
            errors.Add("\"match\" is missing");
        }
        else if (WildcardPattern.LiteralLength(match) < MinimumLiteralLength)
        {
            errors.Add($"\"match\" pattern \"{match}\" is too broad");
        }

        if (name is null)
        {
            errors.Add("\"name\" is missing");
        }

        if (description is null)
        {
            errors.Add("\"description\" is missing");
        }

        Recommendation recommendation = default;
        switch (recommendationText?.ToLowerInvariant())
        {
            case "remove":
                recommendation = Recommendation.Remove;
                break;
            case "optional":
                recommendation = Recommendation.Optional;
                break;
            case "keep":
                recommendation = Recommendation.Keep;
                break;
            default:
                errors.Add("\"recommendation\" must be \"remove\", \"optional\" or \"keep\"");
                break;
        }

        if (publisher is not null && kind == AppKind.StoreApp)
        {
            errors.Add("\"publisher\" only applies to desktop programs");
        }

        var vendorTool = VendorRemovalTool.None;
        switch (vendorToolText?.ToLowerInvariant())
        {
            case null:
                break;
            case "mcafee":
                vendorTool = VendorRemovalTool.McAfee;
                break;
            case "norton":
                vendorTool = VendorRemovalTool.Norton;
                break;
            default:
                errors.Add("\"vendorTool\" must be \"mcafee\" or \"norton\"");
                break;
        }

        bool isAntivirus = false;
        if (item.TryGetProperty("antivirus", out var antivirusElement))
        {
            if (antivirusElement.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                isAntivirus = antivirusElement.GetBoolean();
            }
            else
            {
                errors.Add("\"antivirus\" must be true or false");
            }
        }

        if (errors.Count > 0)
        {
            problems.Add($"{label}: {string.Join("; ", errors)}. Skipped.");
            return null;
        }

        var protection = ProtectedList.CheckPattern(kind, match!);
        if (protection.IsProtected)
        {
            problems.Add($"{label}: \"{match}\" is on the protected list and will never be removed ({protection.Reason}). Skipped.");
            return null;
        }

        return new PresetEntry
        {
            Kind = kind,
            Match = match!,
            Publisher = publisher,
            Name = name!,
            Description = description!,
            Recommendation = recommendation,
            // A vendor removal tool only exists for antivirus, so it always triggers the Defender check.
            IsAntivirus = isAntivirus || vendorTool != VendorRemovalTool.None,
            VendorTool = vendorTool,
        };
    }

    private static bool TryParseTier(string? text, out PresetTier tier)
    {
        switch (text?.ToLowerInvariant())
        {
            case "free":
                tier = PresetTier.Free;
                return true;
            case "pro":
                tier = PresetTier.Pro;
                return true;
            default:
                tier = default;
                return false;
        }
    }

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;

    private static void WarnAboutUnknownProperties(JsonElement element, HashSet<string> known, string where, List<string> problems)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!known.Contains(property.Name))
            {
                problems.Add($"{where}: unknown property \"{property.Name}\" was ignored. Check the spelling.");
            }
        }
    }

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$")]
    private static partial Regex IdFormat();
}
