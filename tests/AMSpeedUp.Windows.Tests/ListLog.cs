using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Windows.Tests;

internal sealed class ListLog : IAppLog
{
    public List<string> Lines { get; } = [];

    public string LogDirectory => "";

    public string CurrentLogFile => "";

    public void Write(LogLevel level, string message, Exception? exception = null)
    {
        lock (Lines)
        {
            Lines.Add($"{level}: {message}");
        }
    }
}
