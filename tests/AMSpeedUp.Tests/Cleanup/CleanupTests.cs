using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;

namespace AMSpeedUp.Tests.Cleanup;

public sealed class CleanupLocationsTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private CleanupPaths Paths => new(_temp.Combine("Temp"), _temp.Combine("Windows"), _temp.Combine("Local"));

    private void Folder(params string[] parts) => Directory.CreateDirectory(_temp.Combine(parts));

    [Fact]
    public void Temp_and_Windows_Update_folders_are_the_standard_ones()
    {
        Assert.Equal([_temp.Combine("Temp")], CleanupLocations.FoldersFor(CleanupCategory.UserTemp, Paths));
        Assert.Equal([_temp.Combine("Windows", "Temp")], CleanupLocations.FoldersFor(CleanupCategory.WindowsTemp, Paths));
        Assert.Equal([_temp.Combine("Windows", "SoftwareDistribution", "Download")], CleanupLocations.FoldersFor(CleanupCategory.WindowsUpdate, Paths));
    }

    [Fact]
    public void Edge_caches_are_found_in_every_profile_but_only_the_cache_folders()
    {
        Folder("Local", "Microsoft", "Edge", "User Data", "Default", "Cache", "Cache_Data");
        Folder("Local", "Microsoft", "Edge", "User Data", "Default", "Code Cache");
        Folder("Local", "Microsoft", "Edge", "User Data", "Profile 2", "GPUCache");
        Folder("Local", "Microsoft", "Edge", "User Data", "Default", "Network");
        Folder("Local", "Microsoft", "Edge", "User Data", "System Profile", "Cache", "Cache_Data");

        var folders = CleanupLocations.FoldersFor(CleanupCategory.EdgeCache, Paths).Select(f => f[_temp.Path.Length..]).Order().ToList();

        var edge = Path.Combine("", "Local", "Microsoft", "Edge", "User Data");
        Assert.Equal(
            new[]
            {
                Path.DirectorySeparatorChar + Path.Combine(edge, "Default", "Cache", "Cache_Data"),
                Path.DirectorySeparatorChar + Path.Combine(edge, "Default", "Code Cache"),
                Path.DirectorySeparatorChar + Path.Combine(edge, "Profile 2", "GPUCache"),
            }.Order(),
            folders);
    }

    [Fact]
    public void Chrome_caches_are_found_the_same_way()
    {
        Folder("Local", "Google", "Chrome", "User Data", "Default", "Cache", "Cache_Data");

        Assert.Single(CleanupLocations.FoldersFor(CleanupCategory.ChromeCache, Paths));
    }

    [Fact]
    public void Firefox_caches_are_found_in_each_profile()
    {
        Folder("Local", "Mozilla", "Firefox", "Profiles", "abc123.default-release", "cache2");
        Folder("Local", "Mozilla", "Firefox", "Profiles", "xyz789.old");

        var folders = CleanupLocations.FoldersFor(CleanupCategory.FirefoxCache, Paths);

        Assert.EndsWith(Path.Combine("abc123.default-release", "cache2"), Assert.Single(folders));
    }

    [Fact]
    public void Browsers_that_are_not_installed_have_no_folders()
    {
        Assert.Empty(CleanupLocations.FoldersFor(CleanupCategory.EdgeCache, Paths));
        Assert.Empty(CleanupLocations.FoldersFor(CleanupCategory.FirefoxCache, Paths));
    }

    [Fact]
    public void The_Recycle_Bin_has_no_folders_because_Windows_empties_it()
    {
        Assert.Empty(CleanupLocations.FoldersFor(CleanupCategory.RecycleBin, Paths));
    }
}

public class CleanupInfoTests
{
    [Fact]
    public void Everything_is_ticked_by_default_except_the_Recycle_Bin()
    {
        foreach (var category in Enum.GetValues<CleanupCategory>())
        {
            Assert.Equal(category != CleanupCategory.RecycleBin, CleanupInfo.For(category).TickedByDefault);
        }
    }

    [Theory]
    [InlineData(CleanupCategory.UserTemp)]
    [InlineData(CleanupCategory.WindowsTemp)]
    public void Temporary_files_less_than_a_day_old_are_left_alone(CleanupCategory category)
    {
        Assert.Equal(TimeSpan.FromDays(1), CleanupInfo.For(category).MinimumAge);
    }

    [Fact]
    public void Each_browser_cache_knows_which_program_must_be_closed()
    {
        Assert.Equal("msedge", CleanupInfo.For(CleanupCategory.EdgeCache).BrowserProcess);
        Assert.Equal("chrome", CleanupInfo.For(CleanupCategory.ChromeCache).BrowserProcess);
        Assert.Equal("firefox", CleanupInfo.For(CleanupCategory.FirefoxCache).BrowserProcess);
        Assert.Null(CleanupInfo.For(CleanupCategory.UserTemp).BrowserProcess);
        Assert.Contains("Close Google Chrome", CleanupInfo.For(CleanupCategory.ChromeCache).BlockedBecauseOpen);
    }

    [Fact]
    public void Browser_descriptions_say_passwords_and_bookmarks_are_safe()
    {
        Assert.Contains("passwords, bookmarks and history aren't touched", CleanupInfo.For(CleanupCategory.EdgeCache).Description);
    }

    [Fact]
    public void Windows_Update_size_is_shown_as_a_minimum()
    {
        Assert.True(CleanupInfo.For(CleanupCategory.WindowsUpdate).SizeIsMinimum);
    }
}

public class CleanupActionTests
{
    private static CleanupTarget Target(CleanupCategory category, long bytes = 1288490189) =>
        new() { Category = category, Folders = [], Bytes = bytes, Files = 10 };

    [Theory]
    [InlineData(CleanupCategory.UserTemp, "Delete your temporary files (1.2 GB)")]
    [InlineData(CleanupCategory.WindowsTemp, "Delete Windows temporary files (1.2 GB)")]
    [InlineData(CleanupCategory.RecycleBin, "Empty the Recycle Bin (1.2 GB)")]
    [InlineData(CleanupCategory.WindowsUpdate, "Tidy up Windows Update leftovers (at least 1.2 GB)")]
    [InlineData(CleanupCategory.EdgeCache, "Clear the Microsoft Edge cache (1.2 GB)")]
    [InlineData(CleanupCategory.FirefoxCache, "Clear the Firefox cache (1.2 GB)")]
    public void Each_kind_is_described_plainly_with_its_size(CleanupCategory category, string expected)
    {
        Assert.Equal(expected, new CleanupAction(Target(category)).Summary);
    }

    [Fact]
    public void Clean_ups_cannot_be_undone_and_need_no_restore_point()
    {
        var plan = ActionPlan.Create("Free up space", [new CleanupAction(Target(CleanupCategory.UserTemp)), new CleanupAction(Target(CleanupCategory.RecycleBin))]);

        Assert.False(plan.NeedsRestorePoint);
        Assert.All(plan.Actions, a => Assert.Equal(UndoSupport.CannotUndo, a.Undo));
        Assert.Contains("deleted for good", plan.CreatePreview(false).Sections.Single().UndoNote);
    }

    [Fact]
    public void Emptying_the_Recycle_Bin_warns_it_is_for_good()
    {
        Assert.Contains("deleted for good", new CleanupAction(Target(CleanupCategory.RecycleBin)).Detail);
    }

    [Theory]
    [InlineData("Image Version: 10.0.22631\r\n[==                         4.0%                           ]", 4.0)]
    [InlineData("[===========================62.3%===                        ]\r[============================100.0%==========================]", 100.0)]
    [InlineData("The operation completed successfully.", null)]
    [InlineData("", null)]
    [InlineData("[ 101.0% ]", null)]
    public void DISM_progress_is_read_from_its_progress_bar(string output, double? expected)
    {
        Assert.Equal(expected, DismProgress.LastPercent(output));
    }
}
