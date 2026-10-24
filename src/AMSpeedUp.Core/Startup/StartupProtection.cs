using AMSpeedUp.Core.Matching;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Core.Startup;

/// <summary>
/// Startup items that can't be switched off from AM Speed Up. Like the removal protected list,
/// it's compiled in and can't be changed by a data file. Switching these back on is always allowed.
/// </summary>
public static class StartupProtection
{
    private const string Security = "Windows Security needs this to protect your PC.";
    private const string Antivirus = "Turning this off could leave your PC without virus protection. Use the antivirus's own settings if you want to change it.";
    private const string Hardware = "Your PC's sound or touchpad may stop working properly without this.";
    private const string Updates = "It keeps a web browser or reader up to date with security fixes.";

    /// <summary>Patterns checked against the item's name, its program file name and its publisher.</summary>
    private static readonly (string Pattern, string Reason)[] Rules =
    [
        ("SecurityHealth*", Security),
        ("Windows Security*", Security),
        ("*Windows Defender*", Security),
        ("*Microsoft Defender*", Security),
        ("MsMpEng*", Security),

        ("*McAfee*", Antivirus),
        ("*Norton*", Antivirus),
        ("*Gen Digital*", Antivirus),
        ("*Avast*", Antivirus),
        ("AVG*", Antivirus),
        ("*Bitdefender*", Antivirus),
        ("*Kaspersky*", Antivirus),
        ("ESET*", Antivirus),
        ("egui*", Antivirus),
        ("*Sophos*", Antivirus),
        ("*Avira*", Antivirus),
        ("*Malwarebytes*", Antivirus),
        ("*Webroot*", Antivirus),
        ("*Trend Micro*", Antivirus),
        ("TotalAV*", Antivirus),

        ("*Realtek*", Hardware),
        ("Rtk*", Hardware),
        ("RTHDVCPL*", Hardware),
        ("*Synaptics*", Hardware),
        ("SynTP*", Hardware),
        ("ETDCtrl*", Hardware),
        ("ELAN*", Hardware),
        ("*Waves*", Hardware),
        ("MaxxAudio*", Hardware),
        ("*Dolby*", Hardware),
        ("*Conexant*", Hardware),

        ("MicrosoftEdgeUpdate*", Updates),
        ("GoogleUpdate*", Updates),
        ("GoogleUpdater*", Updates),
        ("Adobe Acrobat Update*", Updates),
    ];

    /// <summary>Only switching things off is protected. Turning something back on is always fine.</summary>
    public static ProtectionResult CheckTurningOff(StartupItem item)
    {
        string?[] values = [item.Name, item.NameWithoutExtension, item.ProgramFileName, item.Publisher];
        foreach (var (pattern, reason) in Rules)
        {
            if (values.Any(v => v is not null && WildcardPattern.IsMatch(pattern, v)))
            {
                return ProtectionResult.Because(reason);
            }
        }

        return ProtectionResult.NotProtected;
    }
}
