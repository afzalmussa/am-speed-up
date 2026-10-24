using System.Text.Json;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Matching;

namespace AMSpeedUp.Core.Startup;

public enum StartupAdvice
{
    /// <summary>Safe to turn off; the program still works when opened.</summary>
    TurnOff,

    /// <summary>Depends on whether the person uses it.</summary>
    Optional,

    /// <summary>Best left on.</summary>
    KeepOn,
}

/// <param name="Match">Wildcard pattern for the item's name.</param>
/// <param name="File">Wildcard pattern for the program file name, for example "OneDrive.exe".</param>
public sealed record KnownStartupItem(string? Match, string? File, string Name, string Description, StartupAdvice Advice)
{
    public bool IsMatch(StartupItem item) =>
        (Match is null || WildcardPattern.IsMatch(Match, item.Name) || WildcardPattern.IsMatch(Match, item.NameWithoutExtension))
        && (File is null || WildcardPattern.IsMatch(File, item.ProgramFileName));
}

public sealed record KnownStartupParseResult(IReadOnlyList<KnownStartupItem> Items, IReadOnlyList<string> Problems);

/// <summary>
/// Plain descriptions and advice for common startup programs, from data/startup-known.json.
/// Unknown items get the standard "leave it on" advice.
/// </summary>
public static class KnownStartupItems
{
    public const string UnknownDescription = "Not sure what this is? Leave it on.";

    private const string ResourceName = "AMSpeedUp.Core.Data.startup-known.json";

    public static KnownStartupParseResult Parse(string json, string source)
    {
        var problems = new List<string>();
        var items = new List<KnownStartupItem>();

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        }
        catch (JsonException ex)
        {
            problems.Add($"{source}: not valid JSON ({ex.Message}).");
            return new KnownStartupParseResult(items, problems);
        }

        using (document)
        {
            if (!document.RootElement.TryGetProperty("items", out var list) || list.ValueKind != JsonValueKind.Array)
            {
                problems.Add($"{source}: \"items\" must be a list.");
                return new KnownStartupParseResult(items, problems);
            }

            var index = 0;
            foreach (var element in list.EnumerateArray())
            {
                index++;
                var where = $"{source} item {index}";
                if (element.ValueKind != JsonValueKind.Object)
                {
                    problems.Add($"{where}: must be a JSON object. Skipped.");
                    continue;
                }

                var match = Text(element, "match");
                var file = Text(element, "file");
                var name = Text(element, "name");
                var description = Text(element, "description");
                var adviceText = Text(element, "recommendation");

                var errors = new List<string>();
                if (match is null && file is null)
                {
                    errors.Add("needs \"match\" or \"file\"");
                }

                if (match is not null && WildcardPattern.LiteralLength(match) < 3 || file is not null && WildcardPattern.LiteralLength(file) < 3)
                {
                    errors.Add("pattern is too broad");
                }

                if (name is null)
                {
                    errors.Add("\"name\" is missing");
                }

                if (description is null)
                {
                    errors.Add("\"description\" is missing");
                }

                StartupAdvice advice = default;
                switch (adviceText?.ToLowerInvariant())
                {
                    case "turn-off":
                        advice = StartupAdvice.TurnOff;
                        break;
                    case "optional":
                        advice = StartupAdvice.Optional;
                        break;
                    case "keep-on":
                        advice = StartupAdvice.KeepOn;
                        break;
                    default:
                        errors.Add("\"recommendation\" must be \"turn-off\", \"optional\" or \"keep-on\"");
                        break;
                }

                if (errors.Count > 0)
                {
                    problems.Add($"{where} ({name ?? match ?? file}): {string.Join("; ", errors)}. Skipped.");
                    continue;
                }

                items.Add(new KnownStartupItem(match, file, name!, description!, advice));
            }
        }

        return new KnownStartupParseResult(items, problems);
    }

    public static IReadOnlyList<KnownStartupItem> LoadEmbedded(IAppLog log)
    {
        using var stream = typeof(KnownStartupItems).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"Embedded data file '{ResourceName}' is missing.");
        using var reader = new StreamReader(stream);
        var result = Parse(reader.ReadToEnd(), "startup-known.json");
        foreach (var problem in result.Problems)
        {
            log.Warning("Startup list problem: " + problem);
        }

        return result.Items;
    }

    /// <summary>The first known entry that matches, or null for something unknown.</summary>
    public static KnownStartupItem? Find(IEnumerable<KnownStartupItem> known, StartupItem item) => known.FirstOrDefault(k => k.IsMatch(item));

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(value.GetString())
            ? value.GetString()!.Trim()
            : null;
}
