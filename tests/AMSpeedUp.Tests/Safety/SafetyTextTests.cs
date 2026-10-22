using System.Reflection;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Tests.Safety;

public class SafetyTextTests
{
    [Theory]
    [InlineData(RestorePointStatus.TurnedOff, "turned off")]
    [InlineData(RestorePointStatus.TooSoon, "one new restore point a day")]
    [InlineData(RestorePointStatus.Failed, "wasn't able to save one")]
    public void Each_restore_point_problem_has_its_own_explanation(RestorePointStatus status, string expected)
    {
        var message = SafetyText.RestorePointProblem(new RestorePointResult(status));

        Assert.Contains(expected, message.Body);
        Assert.False(string.IsNullOrWhiteSpace(message.Heading));
    }

    [Fact]
    public void Too_soon_says_when_the_existing_restore_point_was_made()
    {
        var when = new DateTimeOffset(2026, 10, 22, 9, 5, 0, TimeSpan.Zero);

        var message = SafetyText.RestorePointProblem(new RestorePointResult(RestorePointStatus.TooSoon, when));

        Assert.Contains(SafetyText.FormatWhen(when), message.Body);
    }

    [Fact]
    public void Turned_off_and_failed_explain_what_a_restore_point_is()
    {
        Assert.Contains(SafetyText.RestorePointExplained, SafetyText.RestorePointProblem(new(RestorePointStatus.TurnedOff)).Body);
        Assert.Contains(SafetyText.RestorePointExplained, SafetyText.RestorePointProblem(new(RestorePointStatus.Failed)).Body);
    }

    [Fact]
    public void Every_undo_level_has_a_note()
    {
        var notes = Enum.GetValues<UndoSupport>().Select(SafetyText.UndoNote).ToList();

        Assert.All(notes, n => Assert.False(string.IsNullOrWhiteSpace(n)));
        Assert.Equal(notes.Count, notes.Distinct().Count());
    }

    [Fact]
    public void Times_are_shown_in_a_friendly_UK_format()
    {
        var text = SafetyText.FormatWhen(new DateTimeOffset(2026, 10, 22, 9, 5, 0, TimeSpan.Zero).ToLocalTime());

        Assert.Matches(@"^\d{1,2}:\d{2} (am|pm) on [A-Z][a-z]+day \d{1,2} [A-Z][a-z]+$", text);
    }

    public static TheoryData<string> AllUserFacingText()
    {
        var data = new TheoryData<string>();
        foreach (var field in typeof(SafetyText).GetFields(BindingFlags.Public | BindingFlags.Static).Where(f => f.FieldType == typeof(string)))
        {
            data.Add((string)field.GetValue(null)!);
        }

        foreach (var status in Enum.GetValues<RestorePointStatus>())
        {
            var m = SafetyText.RestorePointProblem(new RestorePointResult(status, DateTimeOffset.UnixEpoch));
            data.Add(m.Heading);
            data.Add(m.Body);
        }

        foreach (var verdict in Enum.GetValues<AntivirusVerdict>())
        {
            var m = SafetyText.AntivirusWarning(new AntivirusCheckResult(verdict, "X"));
            data.Add(m.Heading);
            data.Add(m.Body);
            data.Add(SafetyText.AntivirusSummary(new AntivirusCheckResult(verdict, "X")));
        }

        foreach (var undo in Enum.GetValues<UndoSupport>())
        {
            data.Add(SafetyText.UndoNote(undo));
        }

        return data;
    }

    // House style: no technical jargon in front of nervous users, and no em dashes.
    [Theory]
    [MemberData(nameof(AllUserFacingText))]
    public void User_facing_text_avoids_jargon(string text)
    {
        string[] jargon = ["PowerShell", "registry", "Appx", "WMI", "exception", "HRESULT", "0x", "uninstall string", "\u2014"];

        Assert.All(jargon, word => Assert.DoesNotContain(word, text, StringComparison.OrdinalIgnoreCase));
    }
}
