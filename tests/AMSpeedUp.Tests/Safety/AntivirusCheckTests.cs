using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Tests.Safety;

public class AntivirusCheckTests
{
    private static readonly DefenderStatus DefenderOn = new(true, true, "Normal");
    private static readonly DefenderStatus DefenderOff = new(true, false, "Normal");
    private static readonly DefenderStatus DefenderPassive = new(true, true, "Passive Mode");

    private static AntivirusSnapshot Snapshot(DefenderStatus? defender, params RegisteredAntivirus[] registered) =>
        new(defender, defender is null ? "query failed" : null, registered, null);

    [Theory]
    [InlineData(397568, true)]  // 0x061100: on, up to date
    [InlineData(397312, true)]  // 0x061000: on, out of date
    [InlineData(266240, true)]  // 0x041000: on (third-party product)
    [InlineData(393472, false)] // 0x060100: off
    [InlineData(262144, false)] // 0x040000: off
    [InlineData(401664, false)] // 0x062100: snoozed, so not on
    public void Security_Center_product_state_is_decoded(int state, bool expectedOn)
    {
        Assert.Equal(expectedOn, new RegisteredAntivirus("Something", state).IsEnabled);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Normal", true)]
    [InlineData("normal", true)]
    [InlineData("Passive Mode", false)]
    [InlineData("SxS Passive Mode", false)]
    [InlineData("EDR Block Mode", false)]
    public void Defender_only_counts_as_protecting_in_normal_mode(string? mode, bool expected)
    {
        Assert.Equal(expected, new DefenderStatus(true, true, mode).IsProtecting);
    }

    [Fact]
    public void Defender_needs_both_antivirus_and_real_time_protection_on()
    {
        Assert.False(new DefenderStatus(false, true, "Normal").IsProtecting);
        Assert.False(new DefenderStatus(true, false, "Normal").IsProtecting);
    }

    [Fact]
    public void Defender_on_is_fine()
    {
        var result = AntivirusCheck.Evaluate(Snapshot(DefenderOn), ["McAfee"]);

        Assert.Equal(AntivirusVerdict.DefenderOn, result.Verdict);
        Assert.False(result.NeedsWarning);
    }

    [Fact]
    public void Defender_off_with_nothing_else_needs_a_warning()
    {
        var result = AntivirusCheck.Evaluate(Snapshot(DefenderOff), ["McAfee"]);

        Assert.Equal(AntivirusVerdict.Unprotected, result.Verdict);
        Assert.True(result.NeedsWarning);
    }

    [Fact]
    public void Defender_off_but_another_antivirus_on_is_fine_and_named()
    {
        var result = AntivirusCheck.Evaluate(Snapshot(DefenderPassive, new RegisteredAntivirus("Bitdefender Total Security", 266240)), ["McAfee"]);

        Assert.Equal(AntivirusVerdict.OtherAntivirusOn, result.Verdict);
        Assert.Equal("Bitdefender Total Security", result.OtherAntivirusName);
        Assert.False(result.NeedsWarning);
    }

    [Fact]
    public void The_antivirus_just_removed_does_not_count_even_if_Windows_still_lists_it()
    {
        var result = AntivirusCheck.Evaluate(Snapshot(DefenderOff, new RegisteredAntivirus("McAfee LiveSafe", 397568)), ["McAfee"]);

        Assert.Equal(AntivirusVerdict.Unprotected, result.Verdict);
    }

    [Fact]
    public void Another_antivirus_that_is_switched_off_does_not_count()
    {
        var result = AntivirusCheck.Evaluate(Snapshot(DefenderOff, new RegisteredAntivirus("Avast Free Antivirus", 393472)), []);

        Assert.Equal(AntivirusVerdict.Unprotected, result.Verdict);
    }

    [Fact]
    public void Defender_listed_in_Security_Center_does_not_count_as_another_antivirus()
    {
        var result = AntivirusCheck.Evaluate(Snapshot(DefenderOff, new RegisteredAntivirus("Windows Defender", 397568)), []);

        Assert.Equal(AntivirusVerdict.Unprotected, result.Verdict);
    }

    [Theory]
    [InlineData("Windows Defender", true)]
    [InlineData("Microsoft Defender Antivirus", true)]
    [InlineData("Windows Security", true)]
    [InlineData("Bitdefender Total Security", false)]
    [InlineData("Bitdefender Antivirus Free", false)]
    [InlineData("Webroot SecureAnywhere", false)]
    public void Microsoft_Defender_is_recognised_by_its_full_name(string name, bool expected)
    {
        Assert.Equal(expected, new RegisteredAntivirus(name, 397568).IsMicrosoftDefender);
    }

    [Fact]
    public void If_Defender_cannot_be_read_and_nothing_else_protects_the_user_is_warned()
    {
        var result = AntivirusCheck.Evaluate(Snapshot(null), ["Norton"]);

        Assert.Equal(AntivirusVerdict.Unknown, result.Verdict);
        Assert.True(result.NeedsWarning);
    }

    [Fact]
    public void If_Security_Center_cannot_be_read_the_Defender_result_still_decides()
    {
        var snapshot = new AntivirusSnapshot(DefenderOff, null, null, "not available");

        Assert.Equal(AntivirusVerdict.Unprotected, AntivirusCheck.Evaluate(snapshot, []).Verdict);
    }

    [Fact]
    public void Every_verdict_has_a_summary_and_warnings_have_a_message()
    {
        foreach (var verdict in Enum.GetValues<AntivirusVerdict>())
        {
            var result = new AntivirusCheckResult(verdict, "Bitdefender");
            Assert.False(string.IsNullOrWhiteSpace(SafetyText.AntivirusSummary(result)));
            var message = SafetyText.AntivirusWarning(result);
            Assert.False(string.IsNullOrWhiteSpace(message.Heading));
            Assert.Contains("Open Windows Security", message.Body);
        }
    }
}
