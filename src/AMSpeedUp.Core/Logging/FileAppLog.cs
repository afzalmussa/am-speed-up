using System.Globalization;
using System.Text;

namespace AMSpeedUp.Core.Logging;

/// <summary>
/// Writes one plain-text log file per day. Lines are flushed straight away so nothing is lost
/// if the app or the PC stops unexpectedly, and the file is opened with sharing so it can be
/// read in Notepad while the app is running.
/// </summary>
public sealed class FileAppLog : IAppLog
{
    private readonly object _gate = new();
    private readonly Func<DateTimeOffset> _clock;
    private readonly bool _dryRun;

    public FileAppLog(string logDirectory, bool dryRun, Func<DateTimeOffset>? clock = null)
    {
        LogDirectory = logDirectory;
        _dryRun = dryRun;
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    public string LogDirectory { get; }

    public string CurrentLogFile => FileFor(_clock());

    /// <summary>
    /// The standard log folder: %ProgramData%\AMSoftware\SpeedUp\logs on Windows.
    /// </summary>
    public static string DefaultDirectory =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "AMSoftware",
            "SpeedUp",
            "logs");

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        var now = _clock();
        var line = Format(now, level, message, exception, _dryRun);

        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                using var stream = new FileStream(FileFor(now), FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                writer.Write(line);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Logging must never crash the app. Writing to the debugger is the best we can do here.
                System.Diagnostics.Debug.WriteLine($"Could not write to the log: {ex.Message}. Line was: {line}");
            }
        }
    }

    internal static string Format(DateTimeOffset when, LogLevel level, string message, Exception? exception, bool dryRun)
    {
        var builder = new StringBuilder();
        builder.Append(when.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(level switch
        {
            LogLevel.Info => "[INFO]   ",
            LogLevel.Warning => "[WARNING]",
            LogLevel.Error => "[ERROR]  ",
            _ => "[?]      ",
        });
        builder.Append(' ');
        if (dryRun)
        {
            builder.Append("[DRY RUN] ");
        }

        builder.Append(message.ReplaceLineEndings(" | "));
        builder.AppendLine();

        if (exception is not null)
        {
            foreach (var detail in exception.ToString().ReplaceLineEndings("\n").Split('\n'))
            {
                builder.Append("    ").AppendLine(detail);
            }
        }

        return builder.ToString();
    }

    private string FileFor(DateTimeOffset when) =>
        Path.Combine(LogDirectory, $"speedup-{when.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.log");
}
