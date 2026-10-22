namespace AMSpeedUp.Core.Logging;

public enum LogLevel
{
    Info,
    Warning,
    Error,
}

/// <summary>
/// Everything the app does, succeeds at or fails at goes through here (safety rule 6).
/// </summary>
public interface IAppLog
{
    /// <summary>Folder holding the log files, shown to the user by "View log".</summary>
    string LogDirectory { get; }

    /// <summary>Today's log file.</summary>
    string CurrentLogFile { get; }

    void Write(LogLevel level, string message, Exception? exception = null);
}

public static class AppLogExtensions
{
    public static void Info(this IAppLog log, string message) => log.Write(LogLevel.Info, message);

    public static void Warning(this IAppLog log, string message, Exception? exception = null) =>
        log.Write(LogLevel.Warning, message, exception);

    public static void Error(this IAppLog log, string message, Exception? exception = null) =>
        log.Write(LogLevel.Error, message, exception);
}
