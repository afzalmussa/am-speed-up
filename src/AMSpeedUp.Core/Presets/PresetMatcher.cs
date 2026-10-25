using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Matching;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Core.Presets;

/// <summary>One installed item that a preset suggests doing something about.</summary>
public sealed record MatchedApp(InstalledApp App, PresetEntry Entry, Recommendation Recommendation, bool IsLocked)
{
    /// <summary>
    /// The preset's friendly name when the entry is for one specific app. When an entry covers
    /// several apps (a wildcard), each app's own name, so they can be told apart.
    /// </summary>
    public string DisplayName => Matching.WildcardPattern.HasWildcards(Entry.Match) ? App.DisplayName : Entry.Name;

    /// <summary>Only "remove" items start ticked, and never Pro items on the free version.</summary>
    public bool IsTickedByDefault => Recommendation == Recommendation.Remove && !IsLocked;
}

public sealed record PresetGroup(Preset Preset, bool IsLocked, IReadOnlyList<MatchedApp> Items);

public sealed record ProtectedMatch(InstalledApp App, string Reason);

public sealed record MatchResult(IReadOnlyList<PresetGroup> Groups, IReadOnlyList<ProtectedMatch> ProtectedSkipped);

public static class PresetMatcher
{
    /// <summary>
    /// Matches what's installed against the presets. Each installed item appears once. If several
    /// presets match the same item, it stays in the first preset that matched, but the most cautious
    /// recommendation wins (keep beats optional beats remove). Protected items never appear.
    /// </summary>
    public static MatchResult Match(IEnumerable<Preset> presets, IEnumerable<InstalledApp> installed, bool isPro)
    {
        var presetList = presets.ToList();
        var protectedSkipped = new List<ProtectedMatch>();
        var placements = new Dictionary<InstalledApp, (int PresetIndex, PresetEntry Entry, Recommendation Recommendation)>();

        foreach (var app in installed)
        {
            for (var p = 0; p < presetList.Count; p++)
            {
                foreach (var entry in presetList[p].Entries)
                {
                    if (!IsMatch(entry, app))
                    {
                        continue;
                    }

                    if (placements.TryGetValue(app, out var existing))
                    {
                        var cautious = MostCautious(existing.Recommendation, entry.Recommendation);
                        placements[app] = existing with { Recommendation = cautious };
                    }
                    else
                    {
                        placements[app] = (p, entry, entry.Recommendation);
                    }
                }
            }
        }

        var groups = new List<PresetGroup>();
        for (var p = 0; p < presetList.Count; p++)
        {
            var preset = presetList[p];
            var locked = preset.Tier == PresetTier.Pro && !isPro;
            var items = new List<MatchedApp>();

            foreach (var (app, placement) in placements.Where(x => x.Value.PresetIndex == p))
            {
                var protection = ProtectedList.Check(app);
                if (protection.IsProtected)
                {
                    protectedSkipped.Add(new ProtectedMatch(app, protection.Reason!));
                    continue;
                }

                items.Add(new MatchedApp(app, placement.Entry, placement.Recommendation, locked));
            }

            if (items.Count > 0)
            {
                items.Sort((a, b) => StringComparer.CurrentCultureIgnoreCase.Compare(a.DisplayName, b.DisplayName));
                groups.Add(new PresetGroup(preset, locked, items));
            }
        }

        return new MatchResult(groups, protectedSkipped);
    }

    public static bool IsMatch(PresetEntry entry, InstalledApp app)
    {
        if (entry.Kind != app.Kind)
        {
            return false;
        }

        if (app.Kind == AppKind.StoreApp)
        {
            return WildcardPattern.IsMatch(entry.Match, app.Id);
        }

        return WildcardPattern.IsMatch(entry.Match, app.DisplayName)
               && (entry.Publisher is null || WildcardPattern.IsMatch(entry.Publisher, app.Publisher));
    }

    public static Recommendation MostCautious(Recommendation a, Recommendation b) => (Recommendation)Math.Max((int)a, (int)b);
}
