using AMSpeedUp.Core.Startup;

namespace AMSpeedUp.Tests.Startup;

public class StartupApprovedTests
{
    [Fact]
    public void No_value_means_on()
    {
        Assert.True(StartupApproved.IsEnabled(null));
        Assert.True(StartupApproved.IsEnabled([]));
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 0x03, 0, 0, 0, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x01 }, false)]
    [InlineData(new byte[] { 0x07, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 0x01 }, false)]
    public void An_odd_first_byte_means_off(byte[] value, bool expected)
    {
        Assert.Equal(expected, StartupApproved.IsEnabled(value));
    }

    [Fact]
    public void The_on_value_matches_Task_Manager()
    {
        var value = StartupApproved.EnabledValue();

        Assert.Equal(12, value.Length);
        Assert.Equal(0x02, value[0]);
        Assert.All(value.Skip(1), b => Assert.Equal(0, b));
        Assert.True(StartupApproved.IsEnabled(value));
    }

    [Fact]
    public void The_off_value_records_when_it_was_switched_off()
    {
        var when = new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero);

        var value = StartupApproved.DisabledValue(when);

        Assert.Equal(12, value.Length);
        Assert.Equal(0x03, value[0]);
        Assert.Equal([0, 0, 0], value[1..4]);
        Assert.Equal(when.UtcDateTime, DateTime.FromFileTimeUtc(BitConverter.ToInt64(value, 4)));
        Assert.False(StartupApproved.IsEnabled(value));
    }

    [Theory]
    [InlineData(StartupSource.RunCurrentUser, false, @"\Run")]
    [InlineData(StartupSource.RunAllUsers, true, @"\Run")]
    [InlineData(StartupSource.RunAllUsers32, true, @"\Run32")]
    [InlineData(StartupSource.StartupFolderCurrentUser, false, @"\StartupFolder")]
    [InlineData(StartupSource.StartupFolderAllUsers, true, @"\StartupFolder")]
    public void Each_source_uses_the_same_key_as_Task_Manager(StartupSource source, bool allUsers, string suffix)
    {
        var item = new StartupItem { Source = source, Name = "Spotify.lnk", IsEnabled = true };

        var location = StartupApproved.Location(item);

        Assert.NotNull(location);
        Assert.Equal(allUsers, location.Value.AllUsers);
        Assert.Equal(StartupApproved.BaseKey + suffix, location.Value.SubKey);
        Assert.Equal("Spotify.lnk", location.Value.ValueName);
    }

    [Fact]
    public void Scheduled_tasks_do_not_use_StartupApproved()
    {
        Assert.Null(StartupApproved.Location(new StartupItem { Source = StartupSource.ScheduledTask, Name = "Task", IsEnabled = true }));
    }
}
