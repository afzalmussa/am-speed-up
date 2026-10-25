using System.Globalization;
using System.Net;
using System.Text;
using AMSpeedUp.Core.Cleanup;

namespace AMSpeedUp.Core.Reports;

public sealed record ReportFacts(
    DateTimeOffset Created,
    DateTimeOffset SessionStarted,
    string PcName,
    string WindowsDescription,
    string AppVersion,
    Measurement<DriveSpace>? FreeSpace,
    Measurement<int>? StartupOn,
    Measurement<int>? AppsFound,
    IReadOnlyList<SessionChange> Changes,
    IReadOnlyList<SessionChange> TestModeChanges,
    bool IsDryRun);

/// <summary>Pro: a one-page HTML report of the PC before and after this session, to keep or print.</summary>
public static class BeforeAfterReport
{
    public const string FeatureName = "The before and after report";

    private static readonly CultureInfo UkEnglish = CultureInfo.GetCultureInfo("en-GB");

    public static string FileName(DateTimeOffset created, TimeZoneInfo? zone = null) =>
        $"AM Speed Up report {Local(created, zone).ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture)}.html";

    public static string BuildHtml(ReportFacts facts, TimeZoneInfo? zone = null)
    {
        var html = new StringBuilder();
        html.Append("""
            <!DOCTYPE html>
            <html lang="en-GB">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>AM Speed Up report</title>
            <style>
            body { margin: 0; background: #E3E5F1; color: #23243A; font: 16px/1.5 "Segoe UI", Arial, sans-serif; }
            header { background: #1B1C2B; color: #FFFFFF; padding: 24px 32px; }
            header h1 { margin: 0; font-size: 26px; }
            header p { margin: 4px 0 0; color: #B9BCD0; }
            main { max-width: 860px; margin: 24px auto; padding: 0 16px; }
            section { background: #FFFFFF; border-radius: 8px; padding: 20px 24px; margin-bottom: 20px; }
            h2 { margin: 0 0 12px; font-size: 20px; }
            table { width: 100%; border-collapse: collapse; }
            th, td { text-align: left; padding: 10px 8px; border-bottom: 1px solid #E3E5F1; vertical-align: top; }
            th { font-weight: 600; }
            .better { color: #047857; font-weight: 600; }
            .note { color: #5B5E78; font-size: 14px; }
            .test { background: #FFF7E0; border: 2px solid #F0C64A; }
            ul { margin: 0; padding-left: 20px; }
            footer { text-align: center; color: #5B5E78; font-size: 14px; padding: 8px 16px 32px; }
            footer a { color: #23243A; }
            @media print { body { background: #FFFFFF; } section { border: 1px solid #CCCCCC; } }
            </style>
            </head>
            <body>
            """);

        html.Append("<header><h1>AM Speed Up report</h1><p>")
            .Append(Encode(facts.PcName)).Append(", ").Append(Encode(facts.WindowsDescription))
            .Append(". Made ").Append(Encode(When(facts.Created, zone))).Append(".</p></header>\n<main>\n");

        if (facts.IsDryRun)
        {
            html.Append("<section class=\"test\"><h2>Test mode was on</h2><p>Nothing on this PC was changed. ")
                .Append("The list further down shows what would have been changed.</p></section>\n");
        }

        html.Append("<section><h2>Before and after</h2>\n<table>\n<tr><th></th><th>Before</th><th>Now</th><th>Change</th></tr>\n");
        if (facts.FreeSpace is { } space)
        {
            Row(html, $"Free space on drive {space.After.Drive}", ByteSize.Format(space.Before.FreeBytes), ByteSize.Format(space.After.FreeBytes),
                SpaceChange(space.After.FreeBytes - space.Before.FreeBytes));
        }

        if (facts.StartupOn is { } startup)
        {
            Row(html, "Programs that start with Windows", startup.Before.ToString(UkEnglish), startup.After.ToString(UkEnglish), CountChange(startup.After - startup.Before));
        }

        if (facts.AppsFound is { } apps)
        {
            Row(html, "Apps recommended for removal", apps.Before.ToString(UkEnglish), apps.After.ToString(UkEnglish), CountChange(apps.After - apps.Before));
        }

        html.Append("</table>\n<p class=\"note\">\"Before\" is when AM Speed Up first checked, on ")
            .Append(Encode(When(facts.SessionStarted, zone)))
            .Append(" or later. Free space also goes up and down as Windows and other programs work, so not every change is down to AM Speed Up.</p>\n");
        if (facts.FreeSpace is null && facts.StartupOn is null && facts.AppsFound is null)
        {
            html.Append("<p>Nothing was checked this session.</p>\n");
        }

        html.Append("</section>\n");

        html.Append("<section><h2>What was changed</h2>\n");
        AppendChanges(html, facts.Changes, "Nothing was changed this session.", zone);
        html.Append("</section>\n");

        if (facts.TestModeChanges.Count > 0)
        {
            html.Append("<section><h2>What would have been changed (test mode)</h2>\n");
            AppendChanges(html, facts.TestModeChanges, "", zone);
            html.Append("</section>\n");
        }

        html.Append("</main>\n<footer>Made by AM Speed Up ").Append(Encode(facts.AppVersion))
            .Append(" from AM Software, <a href=\"https://www.amsoftware.uk\">amsoftware.uk</a>.</footer>\n</body>\n</html>\n");
        return html.ToString();
    }

    private static void AppendChanges(StringBuilder html, IReadOnlyList<SessionChange> changes, string empty, TimeZoneInfo? zone)
    {
        if (changes.Count == 0)
        {
            html.Append("<p>").Append(Encode(empty)).Append("</p>\n");
            return;
        }

        html.Append("<ul>\n");
        foreach (var change in changes)
        {
            html.Append("<li>").Append(Encode(change.Summary)).Append(" <span class=\"note\">(")
                .Append(Encode(Local(change.When, zone).ToString("HH:mm", UkEnglish))).Append(")</span></li>\n");
        }

        html.Append("</ul>\n");
    }

    private static void Row(StringBuilder html, string label, string before, string after, (string Text, bool Better) change) =>
        html.Append("<tr><th>").Append(Encode(label)).Append("</th><td>").Append(Encode(before)).Append("</td><td>").Append(Encode(after))
            .Append("</td><td").Append(change.Better ? " class=\"better\"" : "").Append('>').Append(Encode(change.Text)).Append("</td></tr>\n");

    private static (string, bool) SpaceChange(long difference) => difference switch
    {
        > 0 => ($"{ByteSize.Format(difference)} more", true),
        < 0 => ($"{ByteSize.Format(-difference)} less", false),
        _ => ("No change", false),
    };

    // Fewer startup programs or flagged apps is the better direction.
    private static (string, bool) CountChange(int difference) => difference switch
    {
        < 0 => ($"{-difference} fewer", true),
        > 0 => ($"{difference} more", false),
        _ => ("No change", false),
    };

    private static string When(DateTimeOffset when, TimeZoneInfo? zone) =>
        Local(when, zone).ToString("d MMMM yyyy 'at' HH:mm", UkEnglish);

    private static DateTimeOffset Local(DateTimeOffset when, TimeZoneInfo? zone) =>
        TimeZoneInfo.ConvertTime(when, zone ?? TimeZoneInfo.Local);

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
