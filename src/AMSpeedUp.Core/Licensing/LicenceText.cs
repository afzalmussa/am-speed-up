using System.Globalization;

namespace AMSpeedUp.Core.Licensing;

/// <summary>Everything the user is told about their licence, in one place.</summary>
public static class LicenceText
{
    private static readonly CultureInfo UkEnglish = CultureInfo.GetCultureInfo("en-GB");

    public const string KeyLooksWrong = "That doesn't look like a licence key. Copy it from your receipt email and paste it in.";

    public const string Activated = "Pro is now switched on for this PC. Thank you for supporting AM Speed Up.";

    public const string AlreadyActive = "Pro is already switched on for this PC with this key.";

    public const string Removed = "Pro has been removed from this PC. You can use your licence key on another PC now.";

    public const string RemoveNeedsInternet =
        "AM Speed Up needs to be online to remove Pro from this PC, so the space can be used on another PC. Check your internet connection and try again.";

    public static string ActivationProblem(LicenceOutcome outcome) => outcome switch
    {
        LicenceOutcome.NotFound => "That licence key wasn't recognised. Check you've copied all of it from your receipt email.",
        LicenceOutcome.Expired => "That licence key has expired.",
        LicenceOutcome.Disabled => "That licence key has been switched off. If you think this is a mistake, please get in touch.",
        LicenceOutcome.LimitReached =>
            "That licence key is already being used on as many PCs as it allows. On a PC you no longer use, open AM Speed Up, click \"Enter licence key\" and then \"Remove this PC\". Then try again here.",
        LicenceOutcome.WrongProduct => "That's a licence key for a different product. Please use the key from your AM Speed Up Pro receipt.",
        LicenceOutcome.NoAnswer => "AM Speed Up couldn't reach the licence server. Check your internet connection and try again.",
        _ => "Something went wrong. Please try again.",
    };

    /// <summary>The short label shown at the top of the window.</summary>
    public static string Badge(LicenceStatus status) => status.State switch
    {
        LicenceState.Pro => "Pro",
        LicenceState.ProOffline => "Pro",
        LicenceState.DeveloperMode => "Pro (developer mode)",
        LicenceState.NeedsCheck => "Pro paused",
        LicenceState.Expired => "Pro expired",
        _ => "Free version",
    };

    public static string Describe(LicenceStatus status) => status.State switch
    {
        LicenceState.Free => "You're using the free version. All the safety features are included.",
        LicenceState.Pro => "Pro is switched on for this PC.",
        LicenceState.ProOffline =>
            "Pro is switched on. AM Speed Up couldn't check your licence online recently, but Pro keeps working while it tries again.",
        LicenceState.NeedsCheck =>
            "AM Speed Up hasn't been able to check your licence online for over 30 days, so Pro is paused. Connect to the internet and click \"Check now\". Your free features and safety features still work.",
        LicenceState.Expired => "Your licence has expired, so this PC is back on the free version.",
        LicenceState.Revoked =>
            "Your licence is no longer active on this PC, so it's back on the free version. If you think this is a mistake, please get in touch.",
        LicenceState.DeveloperMode => "Developer mode: Pro is switched on without a licence. This never happens in the version people download.",
        _ => "",
    };

    /// <summary>For example "Used on 2 of 3 PCs."</summary>
    public static string? Activations(StoredLicence? licence) => licence switch
    {
        { ActivationLimit: > 0 and var limit, ActivationUsage: { } usage } => $"Used on {usage} of {limit} PC{(limit == 1 ? "" : "s")}.",
        _ => null,
    };

    public static string? LastChecked(StoredLicence? licence) =>
        licence is null ? null : $"Last checked {licence.LastConfirmedAt.ToLocalTime().ToString("d MMMM yyyy", UkEnglish)}.";

    /// <summary>Keys are never written to the log in full: only the first and last four characters.</summary>
    public static string Mask(string key)
    {
        var trimmed = key.Trim();
        return trimmed.Length <= 8 ? new string('*', trimmed.Length) : $"{trimmed[..4]}...{trimmed[^4..]}";
    }
}
