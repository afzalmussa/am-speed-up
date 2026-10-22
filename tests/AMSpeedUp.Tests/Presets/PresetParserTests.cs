using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Presets;

namespace AMSpeedUp.Tests.Presets;

public class PresetParserTests
{
    private const string ValidPreset = """
        {
          "$schema": "./preset.schema.json",
          "id": "windows-junk",
          "name": "Windows junk apps",
          "description": "Games and promotional apps that come with Windows.",
          "tier": "free",
          "items": [
            {
              "type": "store",
              "match": "king.com.CandyCrush*",
              "name": "Candy Crush games",
              "description": "Promotional games that came with Windows.",
              "recommendation": "remove"
            },
            {
              "type": "desktop",
              "match": "McAfee*",
              "publisher": "McAfee*",
              "name": "McAfee",
              "description": "Trial antivirus. Windows has its own built in.",
              "recommendation": "remove",
              "vendorTool": "mcafee"
            }
          ]
        }
        """;

    private static PresetParseResult ParseWithItems(string itemsJson, string tier = "free") =>
        PresetParser.Parse($$"""
            { "id": "test", "name": "Test", "description": "Test preset", "tier": "{{tier}}", "items": [ {{itemsJson}} ] }
            """, "test.json");

    private static string Item(
        string type = "store",
        string match = "king.com.CandyCrushSaga",
        string recommendation = "remove",
        string extra = "") =>
        $$"""
        { "type": "{{type}}", "match": "{{match}}", "name": "Thing", "description": "A thing.", "recommendation": "{{recommendation}}" {{extra}} }
        """;

    [Fact]
    public void A_valid_preset_parses_with_no_problems()
    {
        var result = PresetParser.Parse(ValidPreset, "windows-junk.json");

        Assert.Empty(result.Problems);
        var preset = Assert.IsType<Preset>(result.Preset);
        Assert.Equal("windows-junk", preset.Id);
        Assert.Equal("Windows junk apps", preset.Name);
        Assert.Equal(PresetTier.Free, preset.Tier);
        Assert.Equal("windows-junk.json", preset.Source);
        Assert.Equal(2, preset.Entries.Count);

        var candy = preset.Entries[0];
        Assert.Equal(AppKind.StoreApp, candy.Kind);
        Assert.Equal("king.com.CandyCrush*", candy.Match);
        Assert.Equal(Recommendation.Remove, candy.Recommendation);
        Assert.False(candy.IsAntivirus);

        var mcafee = preset.Entries[1];
        Assert.Equal(AppKind.DesktopProgram, mcafee.Kind);
        Assert.Equal("McAfee*", mcafee.Publisher);
        Assert.Equal(VendorRemovalTool.McAfee, mcafee.VendorTool);
    }

    [Fact]
    public void A_vendor_removal_tool_always_marks_the_entry_as_antivirus()
    {
        var mcafee = PresetParser.Parse(ValidPreset, "x.json").Preset!.Entries[1];

        Assert.True(mcafee.IsAntivirus);
    }

    [Fact]
    public void Antivirus_flag_can_be_set_without_a_vendor_tool()
    {
        var result = ParseWithItems(Item(type: "desktop", match: "Avast Free*", extra: ", \"antivirus\": true"));

        Assert.True(Assert.Single(result.Preset!.Entries).IsAntivirus);
    }

    [Theory]
    [InlineData("pro", PresetTier.Pro)]
    [InlineData("PRO", PresetTier.Pro)]
    [InlineData("Free", PresetTier.Free)]
    public void Tier_is_read_regardless_of_case(string text, PresetTier expected)
    {
        Assert.Equal(expected, ParseWithItems(Item(), tier: text).Preset!.Tier);
    }

    [Theory]
    [InlineData("")]
    [InlineData("premium")]
    [InlineData("fre")]
    public void An_unknown_tier_rejects_the_whole_preset_rather_than_guessing(string tier)
    {
        var result = ParseWithItems(Item(), tier: tier);

        Assert.Null(result.Preset);
        Assert.Contains(result.Problems, p => p.Contains("\"tier\""));
    }

    [Theory]
    [InlineData("""{ "name": "N", "description": "D", "tier": "free", "items": [] }""", "\"id\"")]
    [InlineData("""{ "id": "Bad Id!", "name": "N", "description": "D", "tier": "free", "items": [] }""", "\"id\"")]
    [InlineData("""{ "id": "ok", "description": "D", "tier": "free", "items": [] }""", "\"name\"")]
    [InlineData("""{ "id": "ok", "name": "N", "tier": "free", "items": [] }""", "\"description\"")]
    [InlineData("""{ "id": "ok", "name": "N", "description": "D", "tier": "free" }""", "\"items\"")]
    [InlineData("""{ "id": "ok", "name": "N", "description": "D", "tier": "free", "items": {} }""", "\"items\"")]
    public void Missing_or_bad_required_fields_reject_the_preset(string json, string mentions)
    {
        var result = PresetParser.Parse(json, "bad.json");

        Assert.Null(result.Preset);
        Assert.Contains(result.Problems, p => p.Contains(mentions) && p.StartsWith("bad.json"));
        Assert.Contains(result.Problems, p => p.Contains("whole preset was skipped"));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{ \"id\": ")]
    [InlineData("[]")]
    [InlineData("\"just a string\"")]
    public void Unreadable_files_are_rejected_with_a_reason(string json)
    {
        var result = PresetParser.Parse(json, "broken.json");

        Assert.Null(result.Preset);
        Assert.Contains(result.Problems, p => p.StartsWith("broken.json") && p.Contains("skipped"));
    }

    [Fact]
    public void Comments_and_trailing_commas_are_allowed()
    {
        var result = PresetParser.Parse("""
            {
              // A comment explaining the preset
              "id": "test", "name": "T", "description": "D", "tier": "free",
              "items": [ { "type": "store", "match": "king.com.CandyCrushSaga", "name": "N", "description": "D", "recommendation": "remove", }, ],
            }
            """, "c.json");

        Assert.Empty(result.Problems);
        Assert.Single(result.Preset!.Entries);
    }

    // Safety rule 3: a preset that tries to remove a protected item.
    [Theory]
    [InlineData("store", "Microsoft.WindowsStore")]
    [InlineData("store", "Microsoft.DesktopAppInstaller")]
    [InlineData("store", "Microsoft.MicrosoftEdge.Stable")]
    [InlineData("store", "Microsoft.SecHealthUI")]
    [InlineData("store", "Microsoft.VCLibs.*")]
    [InlineData("store", "Microsoft.*")]
    [InlineData("desktop", "Microsoft Edge WebView2 Runtime")]
    [InlineData("desktop", "Microsoft Visual C++*")]
    [InlineData("desktop", "Realtek High Definition Audio Driver")]
    public void A_preset_entry_for_a_protected_item_is_skipped_with_a_warning(string type, string match)
    {
        var result = ParseWithItems(Item(type: type, match: match) + "," + Item());

        var preset = Assert.IsType<Preset>(result.Preset);
        var remaining = Assert.Single(preset.Entries);
        Assert.Equal("king.com.CandyCrushSaga", remaining.Match);
        Assert.Contains(result.Problems, p => p.Contains(match) && p.Contains("protected list") && p.EndsWith("Skipped."));
    }

    [Theory]
    [InlineData("*")]
    [InlineData("**")]
    [InlineData("a*")]
    [InlineData("Mic*")]
    [InlineData("???")]
    public void Patterns_that_are_too_broad_are_skipped(string match)
    {
        var result = ParseWithItems(Item(match: match));

        Assert.Empty(result.Preset!.Entries);
        Assert.Contains(result.Problems, p => p.Contains("too broad") || p.Contains("protected list"));
    }

    [Theory]
    [InlineData("""{ "match": "king.com.X", "name": "N", "description": "D", "recommendation": "remove" }""", "\"type\"")]
    [InlineData("""{ "type": "app", "match": "king.com.X", "name": "N", "description": "D", "recommendation": "remove" }""", "\"type\"")]
    [InlineData("""{ "type": "store", "name": "N", "description": "D", "recommendation": "remove" }""", "\"match\"")]
    [InlineData("""{ "type": "store", "match": "king.com.X", "description": "D", "recommendation": "remove" }""", "\"name\"")]
    [InlineData("""{ "type": "store", "match": "king.com.X", "name": "N", "recommendation": "remove" }""", "\"description\"")]
    [InlineData("""{ "type": "store", "match": "king.com.X", "name": "N", "description": "D" }""", "\"recommendation\"")]
    [InlineData("""{ "type": "store", "match": "king.com.X", "name": "N", "description": "D", "recommendation": "delete" }""", "\"recommendation\"")]
    [InlineData("""{ "type": "store", "match": "king.com.X", "name": "N", "description": "D", "recommendation": "remove", "publisher": "King" }""", "\"publisher\"")]
    [InlineData("""{ "type": "desktop", "match": "Avast*", "name": "N", "description": "D", "recommendation": "remove", "vendorTool": "avast" }""", "\"vendorTool\"")]
    [InlineData("""{ "type": "desktop", "match": "Avast*", "name": "N", "description": "D", "recommendation": "remove", "antivirus": "yes" }""", "\"antivirus\"")]
    [InlineData("\"not an object\"", "JSON object")]
    public void A_bad_item_is_skipped_and_the_rest_are_kept(string badItem, string mentions)
    {
        var result = ParseWithItems(badItem + "," + Item());

        Assert.Single(result.Preset!.Entries);
        Assert.Contains(result.Problems, p => p.Contains("item 1") && p.Contains(mentions));
    }

    [Theory]
    [InlineData("remove", Recommendation.Remove)]
    [InlineData("Optional", Recommendation.Optional)]
    [InlineData("KEEP", Recommendation.Keep)]
    public void Recommendations_are_read_regardless_of_case(string text, Recommendation expected)
    {
        Assert.Equal(expected, Assert.Single(ParseWithItems(Item(recommendation: text)).Preset!.Entries).Recommendation);
    }

    [Fact]
    public void Duplicate_items_are_skipped()
    {
        var result = ParseWithItems(Item(match: "king.com.CandyCrushSaga") + "," + Item(match: "KING.COM.CANDYCRUSHSAGA"));

        Assert.Single(result.Preset!.Entries);
        Assert.Contains(result.Problems, p => p.Contains("duplicate"));
    }

    [Fact]
    public void The_same_pattern_can_appear_once_as_store_and_once_as_desktop()
    {
        var result = ParseWithItems(Item(type: "store", match: "Contoso.Thing") + "," + Item(type: "desktop", match: "Contoso.Thing"));

        Assert.Equal(2, result.Preset!.Entries.Count);
    }

    [Fact]
    public void Unknown_properties_are_reported_so_typos_are_noticed()
    {
        var result = ParseWithItems(Item(extra: ", \"recomendation\": \"keep\""));

        Assert.Single(result.Preset!.Entries);
        Assert.Contains(result.Problems, p => p.Contains("\"recomendation\"") && p.Contains("spelling"));
    }

    [Fact]
    public void Values_are_trimmed()
    {
        var result = ParseWithItems("""{ "type": " store ", "match": "  king.com.CandyCrushSaga  ", "name": " Candy ", "description": " D ", "recommendation": " remove " }""");

        var entry = Assert.Single(result.Preset!.Entries);
        Assert.Equal("king.com.CandyCrushSaga", entry.Match);
        Assert.Equal("Candy", entry.Name);
    }

    [Fact]
    public void An_empty_items_list_is_allowed()
    {
        var result = PresetParser.Parse("""{ "id": "empty", "name": "N", "description": "D", "tier": "pro", "items": [] }""", "e.json");

        Assert.Empty(result.Problems);
        Assert.Empty(result.Preset!.Entries);
    }
}
