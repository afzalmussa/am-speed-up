namespace AMSpeedUp.Core.Safety;

/// <summary>What Get-MpComputerStatus says about Microsoft Defender.</summary>
public sealed record DefenderStatus(bool AntivirusEnabled, bool RealTimeProtectionEnabled, string? RunningMode)
{
    /// <summary>
    /// Defender is actively protecting the PC. In "passive mode" it's installed but another
    /// antivirus is in charge, so it doesn't count.
    /// </summary>
    public bool IsProtecting =>
        AntivirusEnabled
        && RealTimeProtectionEnabled
        && (string.IsNullOrWhiteSpace(RunningMode) || RunningMode.Trim().Equals("Normal", StringComparison.OrdinalIgnoreCase));
}

/// <summary>An antivirus registered with Windows Security Center.</summary>
public sealed record RegisteredAntivirus(string Name, int ProductState)
{
    /// <summary>
    /// Decodes the "on" bit of Security Center's productState. The format isn't officially documented
    /// but is widely relied on: bits 12 to 15 are 1 when real-time protection is on.
    /// For example 397568 (0x061100) is on and 393472 (0x060100) is off.
    /// </summary>
    public bool IsEnabled => ((ProductState >> 12) & 0xF) == 1;

    /// <summary>Full product names only: "Bitdefender" also contains the word "defender".</summary>
    public bool IsMicrosoftDefender =>
        Name.Contains("Windows Defender", StringComparison.OrdinalIgnoreCase)
        || Name.Contains("Microsoft Defender", StringComparison.OrdinalIgnoreCase)
        || Name.Trim().Equals("Windows Security", StringComparison.OrdinalIgnoreCase);
}

/// <param name="Defender">Null if Defender couldn't be queried.</param>
/// <param name="Registered">Null if Security Center couldn't be queried.</param>
public sealed record AntivirusSnapshot(
    DefenderStatus? Defender,
    string? DefenderError,
    IReadOnlyList<RegisteredAntivirus>? Registered,
    string? RegisteredError);

public interface IAntivirusStatusService
{
    Task<AntivirusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default);
}

public enum AntivirusVerdict
{
    /// <summary>Microsoft Defender real-time protection is on.</summary>
    DefenderOn,

    /// <summary>Defender is off, but another antivirus that wasn't just removed says it's on.</summary>
    OtherAntivirusOn,

    /// <summary>Nothing is protecting the PC. The user must be told (safety rule 4).</summary>
    Unprotected,

    /// <summary>Couldn't tell. Treated like Unprotected, because staying quiet isn't safe.</summary>
    Unknown,
}

public sealed record AntivirusCheckResult(AntivirusVerdict Verdict, string? OtherAntivirusName = null)
{
    /// <summary>True when the user must see a warning with the "Open Windows Security" button.</summary>
    public bool NeedsWarning => Verdict is AntivirusVerdict.Unprotected or AntivirusVerdict.Unknown;
}

public static class AntivirusCheck
{
    /// <param name="justRemoved">
    /// Names of antivirus products just removed, for example "McAfee". Security Center can keep
    /// listing them for a while afterwards, so they never count as protection.
    /// </param>
    public static AntivirusCheckResult Evaluate(AntivirusSnapshot snapshot, IEnumerable<string> justRemoved)
    {
        if (snapshot.Defender?.IsProtecting == true)
        {
            return new AntivirusCheckResult(AntivirusVerdict.DefenderOn);
        }

        var removed = justRemoved.Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
        var other = snapshot.Registered?
            .Where(av => av.IsEnabled && !av.IsMicrosoftDefender)
            .FirstOrDefault(av => !removed.Any(r => av.Name.Contains(r, StringComparison.OrdinalIgnoreCase)));

        if (other is not null)
        {
            return new AntivirusCheckResult(AntivirusVerdict.OtherAntivirusOn, other.Name);
        }

        if (snapshot.Defender is null)
        {
            return new AntivirusCheckResult(AntivirusVerdict.Unknown);
        }

        return new AntivirusCheckResult(AntivirusVerdict.Unprotected);
    }
}
