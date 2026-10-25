namespace AMSpeedUp.Core.Configuration;

/// <summary>
/// Command-line options. Unknown arguments are kept so they can be written to the log
/// rather than silently ignored.
/// </summary>
public sealed record AppOptions(bool DryRun, IReadOnlyList<string> UnknownArguments)
{
    public const string DryRunFlag = "--dry-run";

    /// <summary>Used once after a removal tool restarts the PC, to check Microsoft Defender is on.</summary>
    public const string CheckAntivirusFlag = "--check-antivirus";

    public static AppOptions Default { get; } = new(false, []);

    public bool CheckAntivirus { get; init; }

    /// <summary>Started by the monthly tune-up scheduled task: clean up with no window, then close.</summary>
    public const string ScheduledTuneUpFlag = "--scheduled-tuneup";

    public bool ScheduledTuneUp { get; init; }

    public static AppOptions Parse(IEnumerable<string>? args)
    {
        var dryRun = false;
        var checkAntivirus = false;
        var scheduledTuneUp = false;
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
                case CheckAntivirusFlag:
                    checkAntivirus = true;
                    break;
                case ScheduledTuneUpFlag:
                    scheduledTuneUp = true;
                    break;
                default:
                    unknown.Add(arg);
                    break;
            }
        }

        return new AppOptions(dryRun, unknown) { CheckAntivirus = checkAntivirus, ScheduledTuneUp = scheduledTuneUp };
    }
}
