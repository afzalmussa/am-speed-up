using AMSpeedUp.Core.Configuration;

namespace AMSpeedUp.Tests.Configuration;

public class AppOptionsTests
{
    [Fact]
    public void No_arguments_means_a_real_run()
    {
        var options = AppOptions.Parse([]);

        Assert.False(options.DryRun);
        Assert.Empty(options.UnknownArguments);
    }

    [Fact]
    public void Null_arguments_are_treated_as_none()
    {
        var options = AppOptions.Parse(null);

        Assert.False(options.DryRun);
        Assert.Empty(options.UnknownArguments);
    }

    [Theory]
    [InlineData("--dry-run")]
    [InlineData("--DRY-RUN")]
    [InlineData("--dryrun")]
    [InlineData("/dry-run")]
    [InlineData("/DryRun")]
    [InlineData("  --dry-run  ")]
    public void Dry_run_flag_is_recognised_in_common_spellings(string flag)
    {
        Assert.True(AppOptions.Parse([flag]).DryRun);
    }

    [Fact]
    public void Unknown_arguments_are_kept_for_the_log_and_do_not_enable_dry_run()
    {
        var options = AppOptions.Parse(["--fast", "hello"]);

        Assert.False(options.DryRun);
        Assert.Equal(["--fast", "hello"], options.UnknownArguments);
    }

    [Fact]
    public void Dry_run_still_applies_when_mixed_with_unknown_arguments()
    {
        var options = AppOptions.Parse(["--something", "--dry-run"]);

        Assert.True(options.DryRun);
        Assert.Equal(["--something"], options.UnknownArguments);
    }

    [Fact]
    public void Blank_arguments_are_ignored()
    {
        var options = AppOptions.Parse(["", "   "]);

        Assert.Empty(options.UnknownArguments);
    }

    [Fact]
    public void A_near_miss_is_not_treated_as_dry_run()
    {
        // "--dry" is not the flag. It must be reported as unknown, not guessed at.
        var options = AppOptions.Parse(["--dry"]);

        Assert.False(options.DryRun);
        Assert.Equal(["--dry"], options.UnknownArguments);
    }
}

public class AppOptionsAntivirusCheckTests
{
    [Fact]
    public void The_after_restart_antivirus_check_flag_is_recognised()
    {
        var options = AppOptions.Parse(["--check-antivirus"]);

        Assert.True(options.CheckAntivirus);
        Assert.False(options.DryRun);
        Assert.Empty(options.UnknownArguments);
    }

    [Fact]
    public void Normal_starts_do_not_check_antivirus()
    {
        Assert.False(AppOptions.Parse([]).CheckAntivirus);
    }
}
