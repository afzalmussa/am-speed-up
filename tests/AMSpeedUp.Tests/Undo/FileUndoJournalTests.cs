using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Undo;
using AMSpeedUp.Tests.Fakes;

namespace AMSpeedUp.Tests.Undo;

public sealed class FileUndoJournalTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly MemoryLog _log = new();

    public void Dispose() => _temp.Dispose();

    private string JournalPath => _temp.Combine("data", "undo-history.json");

    private FileUndoJournal Create() => new(JournalPath, _log);

    private static UndoEntry Entry(string name, UndoKind kind = UndoKind.StoreAppRemoved) => new()
    {
        Id = Guid.NewGuid(),
        When = new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.Zero),
        Kind = kind,
        DisplayName = name,
        Data = new Dictionary<string, string> { ["packageFamilyName"] = name + "_abc" },
        CanUndoInApp = kind == UndoKind.StoreAppRemoved,
    };

    [Fact]
    public void Starts_empty_when_there_is_no_file()
    {
        Assert.Empty(Create().GetAll());
        Assert.False(File.Exists(JournalPath));
    }

    [Fact]
    public void Entries_survive_closing_and_reopening_the_app()
    {
        var candy = Entry("Candy Crush");
        var wild = Entry("WildTangent", UndoKind.DesktopProgramRemoved);
        var journal = Create();
        journal.Add(candy);
        journal.Add(wild);

        var reopened = Create().GetAll();

        Assert.Equal(2, reopened.Count);
        Assert.Equal(candy.Id, reopened[0].Id);
        Assert.Equal(candy.When, reopened[0].When);
        Assert.Equal(UndoKind.StoreAppRemoved, reopened[0].Kind);
        Assert.Equal("Candy Crush_abc", reopened[0].Data["packageFamilyName"]);
        Assert.True(reopened[0].CanUndoInApp);
        Assert.Equal(UndoKind.DesktopProgramRemoved, reopened[1].Kind);
        Assert.False(reopened[1].CanUndoInApp);
    }

    [Fact]
    public void The_file_is_readable_with_named_kinds()
    {
        Create().Add(Entry("Candy Crush"));

        var json = File.ReadAllText(JournalPath);

        Assert.Contains("\"storeAppRemoved\"", json);
        Assert.Contains("\"displayName\": \"Candy Crush\"", json);
    }

    [Fact]
    public void Marking_undone_is_saved()
    {
        var entry = Entry("Candy Crush");
        var journal = Create();
        journal.Add(entry);
        var when = new DateTimeOffset(2026, 10, 23, 8, 0, 0, TimeSpan.Zero);

        Assert.True(journal.MarkUndone(entry.Id, when));

        Assert.Equal(when, Assert.Single(Create().GetAll()).UndoneAt);
    }

    [Fact]
    public void Marking_an_unknown_entry_undone_returns_false()
    {
        Assert.False(Create().MarkUndone(Guid.NewGuid(), DateTimeOffset.UnixEpoch));
    }

    [Fact]
    public void No_temporary_file_is_left_behind()
    {
        Create().Add(Entry("Candy Crush"));

        Assert.False(File.Exists(JournalPath + ".tmp"));
    }

    [Fact]
    public void A_damaged_file_is_kept_aside_and_a_fresh_history_started()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(JournalPath)!);
        File.WriteAllText(JournalPath, "{ this is not valid json");
        var journal = Create();

        Assert.Empty(journal.GetAll());
        journal.Add(Entry("After the damage"));

        Assert.Single(Create().GetAll());
        var backups = Directory.GetFiles(Path.GetDirectoryName(JournalPath)!, "undo-history.json.damaged-*");
        Assert.Equal("{ this is not valid json", File.ReadAllText(Assert.Single(backups)));
        Assert.True(_log.Has(LogLevel.Warning, "damaged"));
    }

    [Fact]
    public void GetAll_returns_a_copy_that_cannot_change_the_journal()
    {
        var journal = Create();
        journal.Add(Entry("Candy Crush"));

        var first = journal.GetAll();
        journal.Add(Entry("Another"));

        Assert.Single(first);
    }
}
