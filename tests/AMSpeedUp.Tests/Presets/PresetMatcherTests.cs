using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Presets;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Presets;

public class PresetMatcherTests
{
    private static readonly InstalledApp CandyCrush = Apps.Store("king.com.CandyCrushSaga", "Candy Crush Saga");
    private static readonly InstalledApp Solitaire = Apps.Store("Microsoft.MicrosoftSolitaireCollection", "Solitaire");
    private static readonly InstalledApp HpSupport = Apps.Desktop("HP Support Assistant", "HP Inc.");
    private static readonly InstalledApp McAfee = Apps.Desktop("McAfee LiveSafe", "McAfee, LLC");

    [Fact]
    public void Remove_items_start_ticked_and_others_do_not()
    {
        var presets = new[]
        {
            Apps.Preset("junk", PresetTier.Free,
                Apps.Entry(AppKind.StoreApp, "king.com.*", Recommendation.Remove),
                Apps.Entry(AppKind.StoreApp, "Microsoft.MicrosoftSolitaireCollection", Recommendation.Optional)),
            Apps.Preset("hp", PresetTier.Free,
                Apps.Entry(AppKind.DesktopProgram, "HP Support Assistant", Recommendation.Keep)),
        };

        var result = PresetMatcher.Match(presets, [CandyCrush, Solitaire, HpSupport], isPro: false);

        var items = result.Groups.SelectMany(g => g.Items).ToDictionary(i => i.App.DisplayName);
        Assert.True(items["Candy Crush Saga"].IsTickedByDefault);
        Assert.False(items["Solitaire"].IsTickedByDefault);
        Assert.False(items["HP Support Assistant"].IsTickedByDefault);
    }

    [Fact]
    public void Pro_presets_are_locked_and_unticked_on_the_free_version()
    {
        var presets = new[] { Apps.Preset("hp", PresetTier.Pro, Apps.Entry(AppKind.DesktopProgram, "HP Support*", Recommendation.Remove)) };

        var group = Assert.Single(PresetMatcher.Match(presets, [HpSupport], isPro: false).Groups);

        Assert.True(group.IsLocked);
        var item = Assert.Single(group.Items);
        Assert.True(item.IsLocked);
        Assert.False(item.IsTickedByDefault);
    }

    [Fact]
    public void Pro_presets_open_up_with_Pro()
    {
        var presets = new[] { Apps.Preset("hp", PresetTier.Pro, Apps.Entry(AppKind.DesktopProgram, "HP Support*", Recommendation.Remove)) };

        var group = Assert.Single(PresetMatcher.Match(presets, [HpSupport], isPro: true).Groups);

        Assert.False(group.IsLocked);
        Assert.True(Assert.Single(group.Items).IsTickedByDefault);
    }

    [Theory]
    [InlineData(Recommendation.Remove, Recommendation.Remove, Recommendation.Remove)]
    [InlineData(Recommendation.Remove, Recommendation.Optional, Recommendation.Optional)]
    [InlineData(Recommendation.Optional, Recommendation.Remove, Recommendation.Optional)]
    [InlineData(Recommendation.Remove, Recommendation.Keep, Recommendation.Keep)]
    [InlineData(Recommendation.Keep, Recommendation.Remove, Recommendation.Keep)]
    [InlineData(Recommendation.Optional, Recommendation.Keep, Recommendation.Keep)]
    public void When_presets_disagree_the_most_cautious_recommendation_wins(Recommendation first, Recommendation second, Recommendation expected)
    {
        var presets = new[]
        {
            Apps.Preset("one", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "king.com.CandyCrushSaga", first)),
            Apps.Preset("two", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "king.com.*", second)),
        };

        var result = PresetMatcher.Match(presets, [CandyCrush], isPro: false);

        var item = Assert.Single(result.Groups.SelectMany(g => g.Items));
        Assert.Equal(expected, item.Recommendation);
        Assert.Equal("one", Assert.Single(result.Groups).Preset.Id);
    }

    [Fact]
    public void Each_installed_item_appears_only_once()
    {
        var presets = new[]
        {
            Apps.Preset("one", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "king.com.CandyCrushSaga"), Apps.Entry(AppKind.StoreApp, "king.com.*")),
            Apps.Preset("two", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "*CandyCrush*")),
        };

        var result = PresetMatcher.Match(presets, [CandyCrush], isPro: false);

        Assert.Single(result.Groups.SelectMany(g => g.Items));
    }

    [Fact]
    public void Protected_items_never_appear_even_if_a_preset_matches_them()
    {
        // Built directly, skipping the parser's own protection check, to prove the matcher checks too.
        var presets = new[] { Apps.Preset("bad", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "Microsoft.Windows*")) };
        var store = Apps.Store("Microsoft.WindowsStore", "Microsoft Store");

        var result = PresetMatcher.Match(presets, [store], isPro: true);

        Assert.Empty(result.Groups);
        var skipped = Assert.Single(result.ProtectedSkipped);
        Assert.Equal(store, skipped.App);
        Assert.False(string.IsNullOrWhiteSpace(skipped.Reason));
    }

    [Fact]
    public void Store_entries_only_match_store_apps_and_desktop_entries_only_desktop_programs()
    {
        var presets = new[] { Apps.Preset("p", PresetTier.Free, Apps.Entry(AppKind.DesktopProgram, "king.com.CandyCrushSaga")) };

        Assert.Empty(PresetMatcher.Match(presets, [CandyCrush], isPro: false).Groups);
    }

    [Fact]
    public void Store_entries_match_the_package_name_not_the_display_name()
    {
        var presets = new[] { Apps.Preset("p", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "Candy Crush Saga")) };

        Assert.Empty(PresetMatcher.Match(presets, [CandyCrush], isPro: false).Groups);
    }

    [Fact]
    public void Desktop_entries_with_a_publisher_need_both_to_match()
    {
        var entry = Apps.Entry(AppKind.DesktopProgram, "McAfee*", publisher: "McAfee*");
        var fake = Apps.Desktop("McAfee LiveSafe", "Not McAfee Ltd");

        Assert.True(PresetMatcher.IsMatch(entry, McAfee));
        Assert.False(PresetMatcher.IsMatch(entry, fake));
        Assert.False(PresetMatcher.IsMatch(entry, Apps.Desktop("McAfee LiveSafe")));
    }

    [Fact]
    public void Presets_with_nothing_installed_are_left_out_and_order_is_kept()
    {
        var presets = new[]
        {
            Apps.Preset("first", PresetTier.Free, Apps.Entry(AppKind.DesktopProgram, "McAfee*")),
            Apps.Preset("empty", PresetTier.Free, Apps.Entry(AppKind.DesktopProgram, "Norton*")),
            Apps.Preset("last", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "king.com.*")),
        };

        var result = PresetMatcher.Match(presets, [CandyCrush, McAfee], isPro: false);

        Assert.Equal(["first", "last"], result.Groups.Select(g => g.Preset.Id));
    }

    [Fact]
    public void Items_in_a_group_are_sorted_by_name()
    {
        var presets = new[]
        {
            Apps.Preset("p", PresetTier.Free,
                Apps.Entry(AppKind.StoreApp, "Contoso.Zebra", name: "Zebra"),
                Apps.Entry(AppKind.StoreApp, "Contoso.Apple", name: "apple")),
        };

        var result = PresetMatcher.Match(presets, [Apps.Store("Contoso.Zebra"), Apps.Store("Contoso.Apple")], isPro: false);

        Assert.Equal(["apple", "Zebra"], result.Groups[0].Items.Select(i => i.Entry.Name));
    }

    [Fact]
    public void Nothing_installed_gives_nothing_found()
    {
        var presets = new[] { Apps.Preset("p", PresetTier.Free, Apps.Entry(AppKind.StoreApp, "king.com.*")) };

        var result = PresetMatcher.Match(presets, [], isPro: false);

        Assert.Empty(result.Groups);
        Assert.Empty(result.ProtectedSkipped);
    }
}
