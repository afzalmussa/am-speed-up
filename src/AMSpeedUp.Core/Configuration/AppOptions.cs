namespace AMSpeedUp.Core.Configuration;

/// <summary>
/// Command-line options. Unknown arguments are kept so they can be written to the log
/// rather than silently ignored.
/// </summary>
public sealed record AppOptions(bool DryRun, IReadOnlyList<string> UnknownArguments)
{
    public const string DryRunFlag = "--dry-run";

    public static AppOptions Default { get; } = new(false, []);

    public static AppOptions Parse(IEnumerable<string>? args)
    {
        var dryRun = false;
        var unknown = new List<string>();

        foreach (var raw in args ?? [])
        {
            var arg = raw.Trim();
            if (arg.Length == 0)
            {
                continue;
            }

            // Accept the common Windows spellings too, so a typo in a shortcut doesn't
            // quietly turn a test run into a real one.
            switch (arg.ToLowerInvariant())
            {
                case DryRunFlag:
                case "--dryrun":
                case "/dry-run":
                case "/dryrun":
                    dryRun = true;
                    break;
                default:
                    unknown.Add(arg);
                    break;
            }
        }

        return new AppOptions(dryRun, unknown);
    }
}
