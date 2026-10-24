using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Startup;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Startup;

public class KnownStartupItemsTests
{
    private static StartupItem Item(string name, string? program = null, StartupSource source = StartupSource.RunCurrentUser) =>
        new() { Source = source, Name = name, ProgramPath = program, IsEnabled = true };

    [Fact]
    public void The_shipped_file_loads_with_no_problems()
    {
        var log = new MemoryLog();

        var known = KnownStartupItems.LoadEmbedded(log);

        Assert.NotEmpty(known);
        Assert.DoesNotContain(log.Entries, e => e.Level != LogLevel.Info);
    }

    [Fact]
    public void The_shipped_file_parses_cleanly_from_the_repo()
    {
        var path = Path.Combine(FindRepoRoot(), "data", "startup-known.json");

        var result = KnownStartupItems.Parse(File.ReadAllText(path), "startup-known.json");

        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Every_shipped_description_is_a_short_plain_sentence()
    {
        foreach (var item in KnownStartupItems.LoadEmbedded(new MemoryLog()))
        {
            Assert.EndsWith(".", item.Description);
            Assert.True(item.Description.Length <= 140, $"{item.Name}: {item.Description.Length} characters");
            Assert.DoesNotContain("registry", item.Description, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("\u2014", item.Description);
        }
    }

    [Fact]
    public void Shipped_advice_never_says_to_turn_off_something_protected()
    {
        var known = KnownStartupItems.LoadEmbedded(new MemoryLog());

        foreach (var entry in known.Where(k => k.Advice == StartupAdvice.TurnOff))
        {
            var example = Item(entry.Match?.Replace("*", "Example") ?? "Example", entry.File is null ? null : @"C:\Program Files\" + entry.File.Replace("*", ""));
            Assert.False(StartupProtection.CheckTurningOff(example).IsProtected, $"{entry.Name} is advised off but protected");
        }
    }

    [Theory]
    [InlineData("OneDrive", @"C:\Program Files\Microsoft OneDrive\OneDrive.exe", "Microsoft OneDrive", StartupAdvice.Optional)]
    [InlineData("Spotify", @"C:\Users\a\AppData\Roaming\Spotify\Spotify.exe", "Spotify", StartupAdvice.TurnOff)]
    [InlineData("MicrosoftEdgeAutoLaunch_0A1B2C3D", @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe", "Microsoft Edge (in the background)", StartupAdvice.TurnOff)]
    [InlineData("SecurityHealth", @"C:\Windows\system32\SecurityHealthSystray.exe", "Windows Security", StartupAdvice.KeepOn)]
    public void Common_programs_get_a_friendly_name_and_advice(string name, string program, string expectedName, StartupAdvice advice)
    {
        var description = StartupDescriber.Describe(Item(name, program), KnownStartupItems.LoadEmbedded(new MemoryLog()));

        Assert.Equal(expectedName, description.DisplayName);
        Assert.Equal(advice, description.Advice);
    }

    [Fact]
    public void Unknown_items_say_leave_it_on()
    {
        var description = StartupDescriber.Describe(Item("Mystery Helper.lnk", @"C:\x\helper.exe", StartupSource.StartupFolderCurrentUser), KnownStartupItems.LoadEmbedded(new MemoryLog()));

        Assert.Equal("Mystery Helper", description.DisplayName);
        Assert.Equal("Not sure what this is? Leave it on.", description.Description);
        Assert.Null(description.Advice);
    }

    [Fact]
    public void Entries_with_both_patterns_need_both_to_match()
    {
        var entry = new KnownStartupItem("Updater", "contoso.exe", "Contoso", "d.", StartupAdvice.TurnOff);

        Assert.True(entry.IsMatch(Item("Updater", @"C:\contoso.exe")));
        Assert.False(entry.IsMatch(Item("Updater", @"C:\other.exe")));
        Assert.False(entry.IsMatch(Item("Other", @"C:\contoso.exe")));
    }

    [Theory]
    [InlineData("""{ "items": [ { "name": "N", "description": "D.", "recommendation": "turn-off" } ] }""", "\"match\" or \"file\"")]
    [InlineData("""{ "items": [ { "match": "*", "name": "N", "description": "D.", "recommendation": "turn-off" } ] }""", "too broad")]
    [InlineData("""{ "items": [ { "match": "Spotify", "description": "D.", "recommendation": "turn-off" } ] }""", "\"name\"")]
    [InlineData("""{ "items": [ { "match": "Spotify", "name": "N", "recommendation": "turn-off" } ] }""", "\"description\"")]
    [InlineData("""{ "items": [ { "match": "Spotify", "name": "N", "description": "D.", "recommendation": "disable" } ] }""", "\"recommendation\"")]
    [InlineData("""{ "items": [ "text" ] }""", "JSON object")]
    public void Bad_entries_are_skipped_with_a_reason(string json, string mentions)
    {
        var result = KnownStartupItems.Parse(json, "test.json");

        Assert.Empty(result.Items);
        Assert.Contains(result.Problems, p => p.Contains(mentions));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("""{ "items": {} }""")]
    public void A_broken_file_gives_no_entries_and_a_problem(string json)
    {
        var result = KnownStartupItems.Parse(json, "test.json");

        Assert.Empty(result.Items);
        Assert.NotEmpty(result.Problems);
    }

    private static string FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "AMSpeedUp.sln")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException("Couldn't find the repo root.");
    }
}
