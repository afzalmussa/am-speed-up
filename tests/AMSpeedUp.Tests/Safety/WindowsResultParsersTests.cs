using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Tests.Safety;

public class WindowsResultParsersTests
{
    [Fact]
    public void Reads_a_successful_restore_point_attempt()
    {
        const string json = """
            {"commandSucceeded":true,"error":null,"warnings":[],"policyDisabled":false,
             "before":{"sequence":41,"created":"2026-10-19T08:00:00.0000000Z"},
             "after":{"sequence":42,"created":"2026-10-22T14:05:00.0000000Z"}}
            """;

        var attempt = WindowsResultParsers.ParseRestorePointAttempt(json);

        Assert.True(attempt.CommandSucceeded);
        Assert.Null(attempt.Error);
        Assert.Empty(attempt.Warnings);
        Assert.Equal(41, attempt.LatestSequenceBefore);
        Assert.Equal(new DateTimeOffset(2026, 10, 19, 8, 0, 0, TimeSpan.Zero), attempt.LatestCreatedBefore);
        Assert.Equal(42, attempt.LatestSequenceAfter);
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 14, 5, 0, TimeSpan.Zero), attempt.LatestCreatedAfter);
    }

    [Fact]
    public void Reads_a_failed_attempt_with_no_restore_points_at_all()
    {
        const string json = """{"commandSucceeded":false,"error":"The service cannot be started","warnings":[],"policyDisabled":true,"before":null,"after":null}""";

        var attempt = WindowsResultParsers.ParseRestorePointAttempt(json);

        Assert.False(attempt.CommandSucceeded);
        Assert.Equal("The service cannot be started", attempt.Error);
        Assert.True(attempt.DisabledByPolicy);
        Assert.Null(attempt.LatestSequenceBefore);
        Assert.Null(attempt.LatestSequenceAfter);
    }

    [Fact]
    public void A_single_warning_written_as_a_plain_string_is_still_read()
    {
        // Windows PowerShell's ConvertTo-Json can flatten a one-item list.
        var attempt = WindowsResultParsers.ParseRestorePointAttempt("""{"commandSucceeded":true,"warnings":"within the past 1440 minutes"}""");

        Assert.Equal(["within the past 1440 minutes"], attempt.Warnings);
    }

    [Fact]
    public void Reads_Defender_and_registered_antivirus()
    {
        const string json = """
            {"defender":{"antivirusEnabled":true,"realTimeProtectionEnabled":false,"amRunningMode":"Passive Mode"},"defenderError":null,
             "products":[{"name":"Windows Defender","state":393472},{"name":"McAfee LiveSafe","state":397568}],"productsError":null}
            """;

        var snapshot = WindowsResultParsers.ParseAntivirusSnapshot(json);

        Assert.Equal(new DefenderStatus(true, false, "Passive Mode"), snapshot.Defender);
        Assert.Null(snapshot.DefenderError);
        Assert.Equal(2, snapshot.Registered!.Count);
        Assert.Equal(new RegisteredAntivirus("McAfee LiveSafe", 397568), snapshot.Registered[1]);
    }

    [Fact]
    public void A_single_registered_product_written_as_an_object_is_still_read()
    {
        var snapshot = WindowsResultParsers.ParseAntivirusSnapshot("""{"defender":null,"defenderError":"x","products":{"name":"Windows Defender","state":397568}}""");

        Assert.Single(snapshot.Registered!);
    }

    [Fact]
    public void Errors_from_either_query_are_kept()
    {
        var snapshot = WindowsResultParsers.ParseAntivirusSnapshot(
            """{"defender":null,"defenderError":"Get-MpComputerStatus failed","products":[],"productsError":"Invalid namespace"}""");

        Assert.Null(snapshot.Defender);
        Assert.Equal("Get-MpComputerStatus failed", snapshot.DefenderError);
        Assert.Null(snapshot.Registered);
        Assert.Equal("Invalid namespace", snapshot.RegisteredError);
    }
}
