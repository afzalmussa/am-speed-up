using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Presets;

/// <summary>Checks the real preset files in the repo's presets folder.</summary>
public class ShippedPresetTests
{
    private static readonly string PresetFolder = FindPresetFolder();

    public static TheoryData<string> PresetFiles()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(PresetFolder, "*.json").Where(f => !f.EndsWith("preset.schema.json", StringComparison.Ordinal)).Order())
        {
            data.Add(Path.GetFileName(file));
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(PresetFiles))]
    public void Every_preset_file_parses_with_no_problems_at_all(string file)
    {
        var result = PresetParser.Parse(File.ReadAllText(Path.Combine(PresetFolder, file)), file);

        Assert.Empty(result.Problems);
        Assert.NotNull(result.Preset);
        Assert.NotEmpty(result.Preset.Entries);
        Assert.Equal(Path.GetFileNameWithoutExtension(file), result.Preset.Id);
    }

    [Fact]
    public void Every_preset_file_is_built_into_the_app()
    {
        var log = new MemoryLog();

        var loaded = PresetLibrary.LoadEmbedded(log);

        Assert.Equal(PresetFiles().Count, loaded.Count);
        Assert.DoesNotContain(log.Entries, e => e.Level != LogLevel.Info);
    }

    [Fact]
    public void Free_presets_come_first()
    {
        var loaded = PresetLibrary.LoadEmbedded(new MemoryLog());

        Assert.Equal(loaded.OrderBy(p => p.Tier).Select(p => p.Id), loaded.Select(p => p.Id));
    }

    [Fact]
    public void Brief_presets_are_free()
    {
        var loaded = PresetLibrary.LoadEmbedded(new MemoryLog()).ToDictionary(p => p.Id);

        Assert.Equal(PresetTier.Free, loaded["windows-junk"].Tier);
        Assert.Equal(PresetTier.Free, loaded["trial-antivirus"].Tier);
    }

    [Fact]
    public void Every_description_is_a_short_sentence_without_jargon()
    {
        string[] jargon = ["appx", "registry", "package", "powershell", "uninstall string", "\u2014"];

        foreach (var entry in PresetLibrary.LoadEmbedded(new MemoryLog()).SelectMany(p => p.Entries))
        {
            Assert.EndsWith(".", entry.Description);
            Assert.True(entry.Description.Length <= 160, $"{entry.Name}: description is {entry.Description.Length} characters");
            Assert.All(jargon, word => Assert.DoesNotContain(word, entry.Description, StringComparison.OrdinalIgnoreCase));
        }
    }

    [Theory]
    [InlineData("McAfee LiveSafe", "McAfee, LLC", VendorRemovalTool.McAfee, true)]
    [InlineData("McAfee Total Protection", "McAfee, LLC", VendorRemovalTool.McAfee, true)]
    [InlineData("McAfee WebAdvisor", "McAfee, LLC", VendorRemovalTool.None, false)]
    [InlineData("McAfee Security Scan Plus", "McAfee, LLC", VendorRemovalTool.None, false)]
    [InlineData("Norton 360", "Gen Digital Inc.", VendorRemovalTool.Norton, true)]
    [InlineData("Norton Security", "NortonLifeLock Inc", VendorRemovalTool.Norton, true)]
    public void Antivirus_entries_pick_the_right_removal_method(string name, string publisher, VendorRemovalTool tool, bool antivirus)
    {
        var presets = PresetLibrary.LoadEmbedded(new MemoryLog());
        var app = Apps.Desktop(name, publisher);

        var match = Assert.Single(PresetMatcher.Match(presets, [app], isPro: false).Groups.SelectMany(g => g.Items));

        Assert.Equal(tool, match.Entry.VendorTool);
        Assert.Equal(antivirus, match.Entry.IsAntivirus);
    }

    [Fact]
    public void A_program_pretending_to_be_McAfee_from_another_publisher_is_not_matched()
    {
        var presets = PresetLibrary.LoadEmbedded(new MemoryLog());

        var result = PresetMatcher.Match(presets, [Apps.Desktop("McAfee LiveSafe", "Totally Legit Software")], isPro: false);

        Assert.Empty(result.Groups);
    }

    [Theory]
    [InlineData("king.com.CandyCrushSaga", Recommendation.Remove)]
    [InlineData("king.com.CandyCrushSodaSaga", Recommendation.Remove)]
    [InlineData("Microsoft.MicrosoftSolitaireCollection", Recommendation.Optional)]
    [InlineData("Microsoft.ZuneMusic", Recommendation.Optional)]
    [InlineData("SpotifyAB.SpotifyMusic", Recommendation.Optional)]
    public void Junk_apps_get_the_expected_recommendation(string package, Recommendation expected)
    {
        var presets = PresetLibrary.LoadEmbedded(new MemoryLog());

        var match = Assert.Single(PresetMatcher.Match(presets, [Apps.Store(package)], isPro: false).Groups.SelectMany(g => g.Items));

        Assert.Equal(expected, match.Recommendation);
    }

    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.Paint")]
    [InlineData("Microsoft.WindowsCalculator")]
    [InlineData("Microsoft.WindowsNotepad")]
    [InlineData("Microsoft.Windows.Photos")]
    [InlineData("Microsoft.ScreenSketch")]
    [InlineData("Microsoft.WindowsCamera")]
    [InlineData("MicrosoftCorporationII.QuickAssist")]
    [InlineData("Microsoft.WindowsTerminal")]
    public void Everyday_Windows_apps_are_never_offered_for_removal(string package)
    {
        var presets = PresetLibrary.LoadEmbedded(new MemoryLog());

        Assert.Empty(PresetMatcher.Match(presets, [Apps.Store(package)], isPro: true).Groups);
    }

    private static string FindPresetFolder()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "presets");
            if (File.Exists(Path.Combine(candidate, "preset.schema.json")))
            {
                return candidate;
            }
        }

        throw new DirectoryNotFoundException("Couldn't find the repo's presets folder.");
    }
}
