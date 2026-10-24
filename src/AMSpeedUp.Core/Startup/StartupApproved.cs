namespace AMSpeedUp.Core.Startup;

/// <summary>
/// The "StartupApproved" registry values Task Manager uses to switch startup items on and off
/// without deleting them. Each value is 12 bytes: a state byte, three padding bytes, then the
/// time it was switched off. An odd first byte (usually 3) means off; even (usually 2 or 6) means
/// on. No value at all means on.
/// </summary>
public static class StartupApproved
{
    public const string BaseKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved";

    public static bool IsEnabled(byte[]? value) => value is not { Length: > 0 } || (value[0] & 1) == 0;

    public static byte[] EnabledValue() => [0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>The value Task Manager writes when switching something off, including when.</summary>
    public static byte[] DisabledValue(DateTimeOffset when)
    {
        var value = new byte[12];
        value[0] = 0x03;
        BitConverter.GetBytes(when.UtcDateTime.ToFileTimeUtc()).CopyTo(value, 4);
        if (!BitConverter.IsLittleEndian)
        {
            Array.Reverse(value, 4, 8);
        }

        return value;
    }

    /// <summary>
    /// Where the on/off value for an item lives, and the value's name. Scheduled tasks don't use
    /// StartupApproved; they're switched on and off in Task Scheduler instead.
    /// </summary>
    public static (bool AllUsers, string SubKey, string ValueName)? Location(StartupItem item) => item.Source switch
    {
        StartupSource.RunCurrentUser => (false, BaseKey + @"\Run", item.Name),
        StartupSource.RunAllUsers => (true, BaseKey + @"\Run", item.Name),
        StartupSource.RunAllUsers32 => (true, BaseKey + @"\Run32", item.Name),
        StartupSource.StartupFolderCurrentUser => (false, BaseKey + @"\StartupFolder", item.Name),
        StartupSource.StartupFolderAllUsers => (true, BaseKey + @"\StartupFolder", item.Name),
        _ => null,
    };
}
