using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Safety;

public class ProtectedListTests
{
    // The minimum list from the brief, plus a few close relatives.
    [Theory]
    [InlineData("Microsoft.WindowsStore")]
    [InlineData("Microsoft.StorePurchaseApp")]
    [InlineData("Microsoft.DesktopAppInstaller")]
    [InlineData("Microsoft.MicrosoftEdge")]
    [InlineData("Microsoft.MicrosoftEdge.Stable")]
    [InlineData("Microsoft.Win32WebViewHost")]
    [InlineData("Microsoft.NET.Native.Framework.2.2")]
    [InlineData("Microsoft.NET.Native.Runtime.2.2")]
    [InlineData("Microsoft.VCLibs.140.00")]
    [InlineData("Microsoft.VCLibs.140.00.UWPDesktop")]
    [InlineData("Microsoft.UI.Xaml.2.8")]
    [InlineData("Microsoft.WindowsAppRuntime.1.5")]
    [InlineData("Microsoft.SecHealthUI")]
    [InlineData("Microsoft.HEVCVideoExtension")]
    [InlineData("Microsoft.HEIFImageExtension")]
    [InlineData("Microsoft.Windows.ShellExperienceHost")]
    [InlineData("MicrosoftWindows.Client.CBS")]
    [InlineData("NVIDIACorp.NVIDIAControlPanel")]
    [InlineData("RealtekSemiconductorCorp.RealtekAudioControl")]
    [InlineData("AppUp.IntelGraphicsExperience")]
    public void Essential_store_apps_are_protected(string packageName)
    {
        var result = ProtectedList.Check(Apps.Store(packageName));

        Assert.True(result.IsProtected, $"{packageName} should be protected");
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
    }

    [Theory]
    [InlineData("Microsoft Edge")]
    [InlineData("Microsoft Edge Update")]
    [InlineData("Microsoft Edge WebView2 Runtime")]
    [InlineData("Microsoft .NET Runtime - 8.0.11 (x64)")]
    [InlineData("Microsoft Windows Desktop Runtime - 8.0.11 (x64)")]
    [InlineData("Microsoft ASP.NET Core 8.0.11 - Shared Framework (x64)")]
    [InlineData("Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810")]
    [InlineData("Microsoft Visual C++ 2010  x86 Redistributable - 10.0.40219")]
    [InlineData("Windows Security")]
    [InlineData("Microsoft Defender Antivirus")]
    [InlineData("Realtek High Definition Audio Driver")]
    [InlineData("NVIDIA Graphics Driver 560.94")]
    [InlineData("Windows Driver Package - Intel (e1dexpress) Net  (12.19.1.37)")]
    [InlineData("Intel(R) Rapid Storage Technology")]
    [InlineData("AMD Chipset Software")]
    [InlineData("Synaptics Pointing Device Driver")]
    [InlineData("Update for Windows 10 for x64-based Systems (KB5001716)")]
    public void Essential_desktop_programs_are_protected(string displayName)
    {
        Assert.True(ProtectedList.Check(Apps.Desktop(displayName)).IsProtected, $"{displayName} should be protected");
    }

    [Theory]
    [InlineData("king.com.CandyCrushSaga")]
    [InlineData("Microsoft.MicrosoftSolitaireCollection")]
    [InlineData("Microsoft.BingNews")]
    [InlineData("Microsoft.ZuneMusic")]
    [InlineData("SpotifyAB.SpotifyMusic")]
    [InlineData("Microsoft.WindowsFeedbackHub")]
    public void Ordinary_store_apps_are_not_protected(string packageName)
    {
        Assert.False(ProtectedList.Check(Apps.Store(packageName)).IsProtected, $"{packageName} should be removable");
    }

    [Theory]
    [InlineData("McAfee LiveSafe", "McAfee, LLC")]
    [InlineData("Norton 360", "Gen Digital Inc.")]
    [InlineData("ExpressVPN", "ExpressVPN")]
    [InlineData("WildTangent Games App", "WildTangent")]
    public void Ordinary_desktop_programs_are_not_protected(string displayName, string publisher)
    {
        Assert.False(ProtectedList.Check(Apps.Desktop(displayName, publisher)).IsProtected, $"{displayName} should be removable");
    }

    [Fact]
    public void Matching_ignores_capital_letters()
    {
        Assert.True(ProtectedList.Check(Apps.Store("microsoft.windowsstore")).IsProtected);
        Assert.True(ProtectedList.Check(Apps.Desktop("MICROSOFT EDGE")).IsProtected);
    }

    [Fact]
    public void Store_apps_signed_as_part_of_Windows_are_protected_whatever_their_name()
    {
        var app = Apps.Store("Contoso.SomethingNew", signature: PackageSignatureKind.System);

        Assert.True(ProtectedList.Check(app).IsProtected);
    }

    [Fact]
    public void Framework_packages_are_protected()
    {
        var app = Apps.Store("Contoso.SharedRuntime") with { IsFramework = true };

        Assert.True(ProtectedList.Check(app).IsProtected);
    }

    [Fact]
    public void Packages_Windows_marks_non_removable_are_protected()
    {
        var app = Apps.Store("Contoso.Pinned") with { IsNonRemovable = true };

        Assert.True(ProtectedList.Check(app).IsProtected);
    }

    [Fact]
    public void Hidden_system_components_are_protected()
    {
        var app = Apps.Desktop("Some Shared Component") with { IsSystemComponent = true };

        Assert.True(ProtectedList.Check(app).IsProtected);
    }

    [Theory]
    [InlineData("Microsoft Windows", null)]
    [InlineData(null, "Microsoft Windows")]
    [InlineData(null, "Microsoft Windows Publisher")]
    public void Desktop_programs_from_or_signed_by_Windows_itself_are_protected(string? publisher, string? signedBy)
    {
        var app = Apps.Desktop("Something Windows Installed", publisher) with { SignedBy = signedBy };

        Assert.True(ProtectedList.Check(app).IsProtected);
    }

    [Fact]
    public void Ordinary_Microsoft_publisher_alone_is_not_enough_to_protect()
    {
        // "Microsoft Corporation" publishes plenty of removable extras. Only core components are protected.
        Assert.False(ProtectedList.Check(Apps.Desktop("Microsoft 365 - en-us", "Microsoft Corporation")).IsProtected);
    }

    [Fact]
    public void Store_rules_match_on_the_family_name_too()
    {
        var app = Apps.Store("Weird.Name") with { PackageFamilyName = "Microsoft.WindowsStore_8wekyb3d8bbwe" };

        Assert.True(ProtectedList.Check(app).IsProtected);
    }

    [Fact]
    public void Store_rules_do_not_apply_to_desktop_programs_and_vice_versa()
    {
        // A desktop program that happens to be called "Microsoft.WindowsStore" isn't the Store app.
        Assert.False(ProtectedList.Check(Apps.Desktop("Microsoft.WindowsStore")).IsProtected);
        Assert.False(ProtectedList.Check(Apps.Store("Contoso.Driver.Helper")).IsProtected);
    }

    public static TheoryData<ProtectionRule, string> EveryRuleExample()
    {
        var data = new TheoryData<ProtectionRule, string>();
        foreach (var rule in ProtectedList.Rules)
        {
            foreach (var example in rule.Examples)
            {
                data.Add(rule, example);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EveryRuleExample))]
    public void Every_example_in_the_list_really_is_protected(ProtectionRule rule, string example)
    {
        var app = rule.Kind == AppKind.StoreApp ? Apps.Store(example) : Apps.Desktop(example);

        Assert.True(ProtectedList.Check(app).IsProtected, $"Rule '{rule.Pattern}' lists '{example}' but doesn't protect it");
    }

    [Fact]
    public void Every_rule_has_a_name_and_a_reason_the_user_can_read()
    {
        Assert.All(ProtectedList.Rules, rule =>
        {
            Assert.False(string.IsNullOrWhiteSpace(rule.FriendlyName));
            Assert.EndsWith(".", rule.Reason);
            Assert.NotEmpty(rule.Examples);
        });
    }

    [Theory]
    [InlineData(AppKind.StoreApp, "Microsoft.WindowsStore")]
    [InlineData(AppKind.StoreApp, "microsoft.windowsstore")]
    [InlineData(AppKind.StoreApp, "Microsoft.Windows*")]
    [InlineData(AppKind.StoreApp, "Microsoft.*")]
    [InlineData(AppKind.StoreApp, "*")]
    [InlineData(AppKind.StoreApp, "*Edge*")]
    [InlineData(AppKind.StoreApp, "Microsoft.VCLibs.140.00")]
    [InlineData(AppKind.DesktopProgram, "Microsoft Edge")]
    [InlineData(AppKind.DesktopProgram, "*Visual C++*")]
    [InlineData(AppKind.DesktopProgram, "*Redistributable*")]
    [InlineData(AppKind.DesktopProgram, "Realtek*")]
    public void Preset_patterns_that_could_reach_protected_items_are_caught(AppKind kind, string pattern)
    {
        Assert.True(ProtectedList.CheckPattern(kind, pattern).IsProtected, $"'{pattern}' should be caught");
    }

    [Theory]
    [InlineData(AppKind.StoreApp, "king.com.CandyCrush*")]
    [InlineData(AppKind.StoreApp, "Microsoft.MicrosoftSolitaireCollection")]
    [InlineData(AppKind.StoreApp, "Microsoft.BingNews")]
    [InlineData(AppKind.DesktopProgram, "McAfee*")]
    [InlineData(AppKind.DesktopProgram, "Norton 360*")]
    public void Ordinary_preset_patterns_are_allowed(AppKind kind, string pattern)
    {
        Assert.False(ProtectedList.CheckPattern(kind, pattern).IsProtected, $"'{pattern}' should be allowed");
    }
}
