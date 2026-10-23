using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Tests.Inventory;

public class StorePackagesTests
{
    private const string Sample = """
        {
          "packages": [
            { "name": "king.com.CandyCrushSaga", "fullName": "king.com.CandyCrushSaga_1.2.3.0_x86__kgqvnymyfvs32", "familyName": "king.com.CandyCrushSaga_kgqvnymyfvs32", "publisher": "CN=King", "signatureKind": "Store", "isFramework": false, "nonRemovable": false, "isResource": false },
            { "name": "Microsoft.ZuneMusic", "fullName": "Microsoft.ZuneMusic_11.0_x64__8wekyb3d8bbwe", "familyName": "Microsoft.ZuneMusic_8wekyb3d8bbwe", "signatureKind": "Store" },
            { "name": "Microsoft.ZuneMusic", "fullName": "Microsoft.ZuneMusic_11.0_x86__8wekyb3d8bbwe", "familyName": "Microsoft.ZuneMusic_8wekyb3d8bbwe", "signatureKind": "Store" },
            { "name": "Microsoft.ZuneMusic", "fullName": "Microsoft.ZuneMusic_11.0_neutral_split.scale-100_8wekyb3d8bbwe", "isResource": true },
            { "name": "Microsoft.VCLibs.140.00", "fullName": "Microsoft.VCLibs.140.00_14.0_x64__8wekyb3d8bbwe", "signatureKind": "Store", "isFramework": true },
            { "name": "Microsoft.Windows.ShellExperienceHost", "fullName": "Microsoft.Windows.ShellExperienceHost_10.0_neutral_neutral_cw5n1h2txyewy", "signatureKind": "System", "nonRemovable": true }
          ],
          "packagesError": null,
          "provisioned": [
            { "name": "king.com.CandyCrushSaga", "packageName": "king.com.CandyCrushSaga_1.2.3.0_neutral_~_kgqvnymyfvs32" },
            { "name": "Microsoft.BingNews", "packageName": "Microsoft.BingNews_4.55.0.0_neutral_~_8wekyb3d8bbwe" }
          ],
          "provisionedError": null
        }
        """;

    [Fact]
    public void Each_app_appears_once_with_all_its_copies()
    {
        var result = StorePackages.Parse(Sample);

        var music = Assert.Single(result.Apps, a => a.Id == "Microsoft.ZuneMusic");
        Assert.Equal(["Microsoft.ZuneMusic_11.0_x64__8wekyb3d8bbwe", "Microsoft.ZuneMusic_11.0_x86__8wekyb3d8bbwe"], music.PackageFullNames);
        Assert.Equal("Zune Music", music.DisplayName);
        Assert.Empty(result.Problems);
    }

    [Fact]
    public void Language_resource_packages_are_ignored()
    {
        var music = StorePackages.Parse(Sample).Apps.Single(a => a.Id == "Microsoft.ZuneMusic");

        Assert.DoesNotContain(music.PackageFullNames, n => n.Contains("split"));
    }

    [Fact]
    public void The_copy_kept_for_new_users_is_linked_to_the_installed_app()
    {
        var candy = StorePackages.Parse(Sample).Apps.Single(a => a.Id == "king.com.CandyCrushSaga");

        Assert.Equal("king.com.CandyCrushSaga_1.2.3.0_neutral_~_kgqvnymyfvs32", candy.ProvisionedPackageName);
        Assert.Equal("king.com.CandyCrushSaga_kgqvnymyfvs32", candy.PackageFamilyName);
        Assert.Equal("Candy Crush Saga", candy.DisplayName);
    }

    [Fact]
    public void Apps_only_kept_for_new_users_are_listed_too()
    {
        var news = StorePackages.Parse(Sample).Apps.Single(a => a.Id == "Microsoft.BingNews");

        Assert.Empty(news.PackageFullNames);
        Assert.Equal("Microsoft.BingNews_4.55.0.0_neutral_~_8wekyb3d8bbwe", news.ProvisionedPackageName);
        Assert.Equal("Microsoft.BingNews_8wekyb3d8bbwe", news.PackageFamilyName);
    }

    [Fact]
    public void Windows_flags_carry_through_so_the_protected_list_can_use_them()
    {
        var apps = StorePackages.Parse(Sample).Apps;

        var vclibs = apps.Single(a => a.Id == "Microsoft.VCLibs.140.00");
        var shell = apps.Single(a => a.Id == "Microsoft.Windows.ShellExperienceHost");
        Assert.True(vclibs.IsFramework);
        Assert.Equal(Core.Inventory.PackageSignatureKind.System, shell.SignatureKind);
        Assert.True(shell.IsNonRemovable);
        Assert.True(ProtectedList.Check(vclibs).IsProtected);
        Assert.True(ProtectedList.Check(shell).IsProtected);
    }

    [Fact]
    public void Errors_from_either_list_are_reported()
    {
        var result = StorePackages.Parse("""{ "packages": [], "packagesError": "Access denied", "provisioned": [], "provisionedError": "DISM failed" }""");

        Assert.Equal(2, result.Problems.Count);
        Assert.Empty(result.Apps);
    }

    [Fact]
    public void A_single_package_written_as_an_object_is_still_read()
    {
        var result = StorePackages.Parse("""{ "packages": { "name": "king.com.CandyCrushSaga", "fullName": "f" }, "provisioned": null }""");

        Assert.Single(result.Apps);
    }

    [Theory]
    [InlineData("king.com.CandyCrushSaga_1.2.3.0_neutral_~_kgqvnymyfvs32", "king.com.CandyCrushSaga_kgqvnymyfvs32")]
    [InlineData("not-a-full-name", null)]
    public void Family_names_are_worked_out_from_full_names(string fullName, string? expected)
    {
        Assert.Equal(expected, StorePackages.FamilyNameFromFullName(fullName));
    }
}
