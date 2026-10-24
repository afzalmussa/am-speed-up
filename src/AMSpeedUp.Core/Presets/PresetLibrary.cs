using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Core.Presets;

/// <summary>Loads the presets built into the app from the repo's presets folder.</summary>
public static class PresetLibrary
{
    private const string Prefix = "AMSpeedUp.Core.Presets.";

    /// <summary>Free presets first, then Pro, each in name order, so the list looks the same every time.</summary>
    public static IReadOnlyList<Preset> LoadEmbedded(IAppLog log)
    {
        var assembly = typeof(PresetLibrary).Assembly;
        var presets = new List<Preset>();

        foreach (var resource in assembly.GetManifestResourceNames().Where(n => n.StartsWith(Prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            using var stream = assembly.GetManifestResourceStream(resource)!;
            using var reader = new StreamReader(stream);
            var source = resource[Prefix.Length..];
            var result = PresetParser.Parse(reader.ReadToEnd(), source);

            foreach (var problem in result.Problems)
            {
                log.Warning($"Preset problem: {problem}");
            }

            if (result.Preset is null)
            {
                continue;
            }

            if (presets.Any(p => p.Id == result.Preset.Id))
            {
                log.Warning($"Preset problem: {source} uses the id \"{result.Preset.Id}\", which another preset already uses. Skipped.");
                continue;
            }

            presets.Add(result.Preset);
        }

        log.Info($"Loaded {presets.Count} preset(s) with {presets.Sum(p => p.Entries.Count)} entries.");
        return presets.OrderBy(p => p.Tier).ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }
}
