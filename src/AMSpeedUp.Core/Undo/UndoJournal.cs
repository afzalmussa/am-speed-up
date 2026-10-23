using System.Text.Json;
using System.Text.Json.Serialization;
using AMSpeedUp.Core.Logging;

namespace AMSpeedUp.Core.Undo;

public enum UndoKind
{
    StartupItemDisabled,
    StartupItemEnabled,
    StoreAppRemoved,
    DesktopProgramRemoved,
    FilesDeleted,
}

/// <summary>A record of one change, kept so it can be undone (or at least explained) later.</summary>
public sealed record UndoEntry
{
    public required Guid Id { get; init; }

    public required DateTimeOffset When { get; init; }

    public required UndoKind Kind { get; init; }

    public required string DisplayName { get; init; }

    /// <summary>Whatever is needed to undo it, for example a Store package family name.</summary>
    public IReadOnlyDictionary<string, string> Data { get; init; } = new Dictionary<string, string>();

    public bool CanUndoInApp { get; init; }

    public DateTimeOffset? UndoneAt { get; init; }
}

public interface IUndoJournal
{
    IReadOnlyList<UndoEntry> GetAll();

    void Add(UndoEntry entry);

    /// <summary>Returns false if there's no entry with that id.</summary>
    bool MarkUndone(Guid id, DateTimeOffset when);
}

/// <summary>
/// Keeps the undo history in a JSON file. Every write goes to a temporary file first and then
/// replaces the real one, so a power cut can't leave a half-written history.
/// </summary>
public sealed class FileUndoJournal : IUndoJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    private readonly object _gate = new();
    private readonly string _path;
    private readonly IAppLog _log;
    private List<UndoEntry>? _entries;

    public FileUndoJournal(string path, IAppLog log)
    {
        _path = path;
        _log = log;
    }

    public IReadOnlyList<UndoEntry> GetAll()
    {
        lock (_gate)
        {
            return Load().ToList();
        }
    }

    public void Add(UndoEntry entry)
    {
        lock (_gate)
        {
            Load().Add(entry);
            Save();
        }
    }

    public bool MarkUndone(Guid id, DateTimeOffset when)
    {
        lock (_gate)
        {
            var entries = Load();
            var index = entries.FindIndex(e => e.Id == id);
            if (index < 0)
            {
                return false;
            }

            entries[index] = entries[index] with { UndoneAt = when };
            Save();
            return true;
        }
    }

    private List<UndoEntry> Load()
    {
        if (_entries is not null)
        {
            return _entries;
        }

        if (!File.Exists(_path))
        {
            return _entries = [];
        }

        try
        {
            var json = File.ReadAllText(_path);
            _entries = JsonSerializer.Deserialize<List<UndoEntry>>(json, JsonOptions) ?? [];
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException)
        {
            // Keep the damaged file for investigation and start a fresh history rather than crash.
            var backup = $"{_path}.damaged-{DateTime.Now:yyyyMMdd-HHmmss}";
            _log.Warning($"The undo history file was damaged and couldn't be read. It has been kept as {backup} and a new one started.", ex);
            TryMove(_path, backup);
            _entries = [];
        }

        return _entries;
    }

    private void Save()
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temp = _path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(_entries, JsonOptions));
        File.Move(temp, _path, overwrite: true);
    }

    private void TryMove(string from, string to)
    {
        try
        {
            File.Move(from, to, overwrite: true);
        }
        catch (IOException ex)
        {
            _log.Warning($"Couldn't keep a copy of the damaged undo history at {to}.", ex);
        }
    }
}
