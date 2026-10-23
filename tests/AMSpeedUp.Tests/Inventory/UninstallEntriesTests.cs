using AMSpeedUp.Core.Inventory;

namespace AMSpeedUp.Tests.Inventory;

public class UninstallEntriesTests
{
    private static UninstallEntry Entry(string key, string? name, Action<UninstallEntryBuilder>? change = null)
    {
        var builder = new UninstallEntryBuilder { KeyName = key, DisplayName = name, UninstallString = "\"C:\\x\\uninstall.exe\"" };
        change?.Invoke(builder);
        return builder.Build();
    }

    [Fact]
    public void Ordinary_programs_are_listed_with_their_details()
    {
        var app = Assert.Single(UninstallEntries.ToInstalledApps([
            Entry("WildTangent wildgames Master Uninstall", " WildTangent Games ", b => { b.Publisher = "WildTangent"; b.Version = "1.1.1.83"; }),
        ]));

        Assert.Equal(AppKind.DesktopProgram, app.Kind);
        Assert.Equal("WildTangent Games", app.DisplayName);
        Assert.Equal("WildTangent", app.Publisher);
        Assert.Equal("1.1.1.83", app.DisplayVersion);
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\WildTangent wildgames Master Uninstall", app.RegistryKeyPath);
        Assert.Equal(app.RegistryKeyPath, app.Id);
        Assert.Null(app.MsiProductCode);
    }

    [Fact]
    public void Entries_without_a_name_are_left_out()
    {
        Assert.Empty(UninstallEntries.ToInstalledApps([Entry("x", null), Entry("y", "   ")]));
    }

    [Theory]
    [InlineData("Update")]
    [InlineData("Security Update")]
    [InlineData("Hotfix")]
    public void Updates_and_patches_are_left_out(string releaseType)
    {
        Assert.Empty(UninstallEntries.ToInstalledApps([Entry("KB1", "Update for Something", b => b.ReleaseType = releaseType)]));
    }

    [Fact]
    public void Child_entries_of_another_program_are_left_out()
    {
        Assert.Empty(UninstallEntries.ToInstalledApps([Entry("child", "Child part", b => b.ParentKeyName = "Parent")]));
    }

    [Fact]
    public void Entries_with_no_way_to_uninstall_are_left_out()
    {
        Assert.Empty(UninstallEntries.ToInstalledApps([Entry("x", "Thing", b => b.UninstallString = null)]));
    }

    [Fact]
    public void Windows_Installer_programs_get_their_product_code()
    {
        const string code = "{ac76ba86-7ad7-1033-7b44-ac0f074e4100}";

        var app = Assert.Single(UninstallEntries.ToInstalledApps([Entry(code, "Adobe Reader", b => { b.WindowsInstaller = 1; b.UninstallString = null; })]));

        Assert.Equal(code.ToUpperInvariant(), app.MsiProductCode);
    }

    [Fact]
    public void A_GUID_key_that_is_not_Windows_Installer_has_no_product_code()
    {
        var app = Assert.Single(UninstallEntries.ToInstalledApps([Entry("{AC76BA86-7AD7-1033-7B44-AC0F074E4100}", "Thing")]));

        Assert.Null(app.MsiProductCode);
    }

    [Fact]
    public void Hidden_system_components_are_flagged()
    {
        var app = Assert.Single(UninstallEntries.ToInstalledApps([Entry("x", "Hidden", b => b.SystemComponent = 1)]));

        Assert.True(app.IsSystemComponent);
    }

    [Fact]
    public void A_program_listed_twice_appears_once()
    {
        var apps = UninstallEntries.ToInstalledApps([
            Entry("a", "Same Program", b => b.Publisher = "Maker"),
            Entry("b", "same program", b => { b.Publisher = "MAKER"; b.Hive = @"HKLM\SOFTWARE\WOW6432Node"; }),
            Entry("c", "Same Program", b => b.Publisher = "Different Maker"),
        ]);

        Assert.Equal(2, apps.Count);
        Assert.Equal(@"HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\a", apps[0].Id);
    }

    public sealed class UninstallEntryBuilder
    {
        public string KeyName { get; set; } = "";
        public string Hive { get; set; } = @"HKLM\SOFTWARE";
        public string? DisplayName { get; set; }
        public string? Publisher { get; set; }
        public string? Version { get; set; }
        public string? UninstallString { get; set; }
        public int? SystemComponent { get; set; }
        public int? WindowsInstaller { get; set; }
        public string? ParentKeyName { get; set; }
        public string? ReleaseType { get; set; }

        public UninstallEntry Build() => new()
        {
            KeyName = KeyName,
            KeyPath = $@"{Hive}\Microsoft\Windows\CurrentVersion\Uninstall\{KeyName}",
            DisplayName = DisplayName,
            Publisher = Publisher,
            DisplayVersion = Version,
            UninstallString = UninstallString,
            SystemComponent = SystemComponent,
            WindowsInstaller = WindowsInstaller,
            ParentKeyName = ParentKeyName,
            ReleaseType = ReleaseType,
        };
    }
}
