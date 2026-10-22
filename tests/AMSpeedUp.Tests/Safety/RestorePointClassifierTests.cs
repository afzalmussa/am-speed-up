using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Tests.Safety;

public class RestorePointClassifierTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void A_new_restore_point_appearing_means_it_was_created()
    {
        var attempt = new RestorePointAttempt
        {
            CommandSucceeded = true,
            LatestSequenceBefore = 41,
            LatestCreatedBefore = Now.AddDays(-3),
            LatestSequenceAfter = 42,
            LatestCreatedAfter = Now,
        };

        var result = RestorePointClassifier.Classify(attempt, Now);

        Assert.Equal(RestorePointStatus.Created, result.Status);
        Assert.Equal(Now, result.When);
    }

    [Fact]
    public void The_first_ever_restore_point_counts_as_created()
    {
        var attempt = new RestorePointAttempt { CommandSucceeded = true, LatestSequenceAfter = 1, LatestCreatedAfter = Now };

        Assert.Equal(RestorePointStatus.Created, RestorePointClassifier.Classify(attempt, Now).Status);
    }

    [Fact]
    public void Windows_saying_success_without_a_new_point_inside_24_hours_means_too_soon()
    {
        // Windows reports success but quietly skips it when one was made in the last 24 hours.
        var earlier = Now.AddHours(-3);
        var attempt = new RestorePointAttempt
        {
            CommandSucceeded = true,
            LatestSequenceBefore = 42,
            LatestCreatedBefore = earlier,
            LatestSequenceAfter = 42,
            LatestCreatedAfter = earlier,
        };

        var result = RestorePointClassifier.Classify(attempt, Now);

        Assert.Equal(RestorePointStatus.TooSoon, result.Status);
        Assert.Equal(earlier, result.When);
    }

    [Fact]
    public void The_1440_minutes_warning_means_too_soon()
    {
        var attempt = new RestorePointAttempt
        {
            CommandSucceeded = true,
            Warnings = ["A new system restore point cannot be created because one has already been created within the past 1440 minutes."],
            LatestSequenceBefore = 7,
            LatestCreatedBefore = Now.AddHours(-1),
            LatestSequenceAfter = 7,
        };

        Assert.Equal(RestorePointStatus.TooSoon, RestorePointClassifier.Classify(attempt, Now).Status);
    }

    [Theory]
    [InlineData("This command cannot be run due to the error: The service cannot be started, either because it is disabled or because it has no enabled devices associated with it.")]
    [InlineData("System Restore is disabled on this computer.")]
    [InlineData("Error 0x80070422")]
    [InlineData("The operation failed with error 1058.")]
    public void Errors_about_System_Restore_being_off_mean_turned_off(string error)
    {
        var attempt = new RestorePointAttempt { CommandSucceeded = false, Error = error };

        var result = RestorePointClassifier.Classify(attempt, Now);

        Assert.Equal(RestorePointStatus.TurnedOff, result.Status);
        Assert.Contains(error, result.TechnicalDetail);
    }

    [Fact]
    public void A_policy_turning_System_Restore_off_means_turned_off()
    {
        var attempt = new RestorePointAttempt { CommandSucceeded = true, DisabledByPolicy = true, LatestSequenceAfter = 5 };

        Assert.Equal(RestorePointStatus.TurnedOff, RestorePointClassifier.Classify(attempt, Now).Status);
    }

    [Fact]
    public void Any_other_error_means_failed_with_the_details_kept_for_the_log()
    {
        var attempt = new RestorePointAttempt { CommandSucceeded = false, Error = "Access is denied." };

        var result = RestorePointClassifier.Classify(attempt, Now);

        Assert.Equal(RestorePointStatus.Failed, result.Status);
        Assert.Contains("Access is denied.", result.TechnicalDetail);
    }

    [Fact]
    public void Success_with_no_new_point_and_no_recent_one_means_failed_not_created()
    {
        // Never trust "success" alone: no new restore point is no restore point.
        var attempt = new RestorePointAttempt
        {
            CommandSucceeded = true,
            LatestSequenceBefore = 3,
            LatestCreatedBefore = Now.AddDays(-10),
            LatestSequenceAfter = 3,
        };

        Assert.Equal(RestorePointStatus.Failed, RestorePointClassifier.Classify(attempt, Now).Status);
    }

    [Fact]
    public void Success_with_nothing_to_compare_means_failed()
    {
        Assert.Equal(RestorePointStatus.Failed, RestorePointClassifier.Classify(new RestorePointAttempt { CommandSucceeded = true }, Now).Status);
    }
}
