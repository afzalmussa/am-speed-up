namespace AMSpeedUp.Core.Platform;

/// <param name="TimedOut">The script was stopped because it took too long.</param>
public sealed record PowerShellResult(int ExitCode, string Output, string Errors, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;
}

/// <summary>
/// Runs Windows PowerShell scripts for things that are best done there (Store apps, Defender status,
/// restore points). Implementations log the purpose, output and errors of every run.
/// </summary>
public interface IPowerShellRunner
{
    /// <param name="purpose">Short description for the log, for example "Checking Microsoft Defender".</param>
    Task<PowerShellResult> RunAsync(string script, string purpose, TimeSpan timeout, CancellationToken cancellationToken = default);
}

public static class PowerShellQuoting
{
    /// <summary>
    /// Turns any text into a PowerShell single-quoted string literal, so values such as app names
    /// can never be run as code. PowerShell also treats curly quotes as quote marks, so those are
    /// doubled too.
    /// </summary>
    public static string Literal(string? value)
    {
        if (value is null)
        {
            return "$null";
        }

        var builder = new System.Text.StringBuilder(value.Length + 2);
        builder.Append('\'');
        foreach (var c in value)
        {
            switch (c)
            {
                case '\0':
                    // Can't appear in a real name and would cut the command short. Drop it.
                    break;
                case '\'':
                case '‘': // left single quotation mark
                case '’': // right single quotation mark
                case '‚': // single low-9 quotation mark
                case '‛': // single high-reversed-9 quotation mark
                    builder.Append(c).Append(c);
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('\'');
        return builder.ToString();
    }
}
