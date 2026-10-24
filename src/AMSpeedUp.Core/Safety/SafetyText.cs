using System.Globalization;

namespace AMSpeedUp.Core.Safety;

/// <summary>A message for the user: a short heading and a few sentences.</summary>
public sealed record UserMessage(string Heading, string Body);

/// <summary>
/// Every safety message the user sees, in one place so the wording is easy to review.
/// Written for people who aren't confident with computers.
/// </summary>
public static class SafetyText
{
    private static readonly CultureInfo UkEnglish = CultureInfo.GetCultureInfo("en-GB");

    public const string RestorePointExplained =
        "A restore point is a snapshot of Windows that lets you undo changes if something goes wrong.";

    public const string RestorePointDescription = "AM Speed Up: before removing apps";

    public static UserMessage RestorePointProblem(RestorePointResult result) => result.Status switch
    {
        RestorePointStatus.TurnedOff => new UserMessage(
            "System Restore is turned off",
            $"Before removing anything, AM Speed Up normally saves a restore point. {RestorePointExplained}\n\n" +
            "System Restore is turned off on this PC, so a restore point can't be saved. " +
            "You can turn it on and then try again, carry on without one, or cancel."),

        RestorePointStatus.TooSoon => new UserMessage(
            "A restore point was saved recently",
            "Windows only saves one new restore point a day. " +
            (result.When is { } when
                ? $"There's already one from {FormatWhen(when)}. "
                : "There's already one from the last 24 hours. ") +
            "If something goes wrong, you can use that one to undo today's changes.\n\n" +
            "Using it would also undo anything else you've changed since it was saved."),

        _ => new UserMessage(
            "Windows couldn't save a restore point",
            $"Before removing anything, AM Speed Up normally saves a restore point. {RestorePointExplained}\n\n" +
            "Windows wasn't able to save one this time. The details have been saved in the log. " +
            "You can carry on without one, or cancel and try again later."),
    };

    public const string ContinueWithoutRestorePoint = "Continue without a restore point";

    public const string ContinueWithExistingRestorePoint = "Continue";

    public const string TurnOnSystemRestore = "Turn on System Restore";

    public static UserMessage AntivirusWarning(AntivirusCheckResult result) => result.Verdict switch
    {
        AntivirusVerdict.Unknown => new UserMessage(
            "Please check your antivirus",
            "AM Speed Up couldn't find out whether Microsoft Defender, the antivirus that comes with Windows, is switched on.\n\n" +
            "Click \"Open Windows Security\" and check that \"Real-time protection\" is on."),

        _ => new UserMessage(
            "Your PC isn't protected against viruses",
            "Microsoft Defender, the antivirus that comes with Windows, is turned off.\n\n" +
            "Click \"Open Windows Security\" and turn on \"Real-time protection\".\n\n" +
            "If you've just removed another antivirus, Windows can take a few minutes to switch Defender on, " +
            "and sometimes needs a restart first. Please check again after restarting your PC."),
    };

    public const string OpenWindowsSecurity = "Open Windows Security";

    public static string AntivirusSummary(AntivirusCheckResult result) => result.Verdict switch
    {
        AntivirusVerdict.DefenderOn => "Microsoft Defender is switched on and protecting your PC.",
        AntivirusVerdict.OtherAntivirusOn => $"{result.OtherAntivirusName} is protecting your PC.",
        AntivirusVerdict.Unknown => "We couldn't check whether your PC is protected against viruses.",
        _ => "Your PC isn't protected against viruses. Please turn on Microsoft Defender.",
    };

    // Preview wording (safety rules 1 and 7)

    public const string PreviewIntro = "Here's exactly what will happen. Nothing changes until you click the button at the bottom.";

    public const string PreviewRestorePointNotice =
        "Before starting, AM Speed Up will save a restore point so you can undo these changes if you need to.";

    public const string PreviewDryRunNotice = "Test mode is on. Nothing will actually be changed.";

    public const string PreviewBlockedHeading = "These are protected and won't be removed";

    public static string UndoNote(Actions.UndoSupport undo) => undo switch
    {
        Actions.UndoSupport.InApp => "You can turn these back on at any time in AM Speed Up.",
        Actions.UndoSupport.ReinstallFromApp =>
            "You can reinstall these later from AM Speed Up or the Microsoft Store.",
        Actions.UndoSupport.RestorePointOnly =>
            "AM Speed Up can't put these back. The only way to undo this is to use the restore point.",
        _ => "These are deleted for good and can't be brought back.",
    };

    public static string FormatWhen(DateTimeOffset when) =>
        when.ToLocalTime().ToString("h:mm tt 'on' dddd d MMMM", UkEnglish).Replace("AM", "am").Replace("PM", "pm");
}
