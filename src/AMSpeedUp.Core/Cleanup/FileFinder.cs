using System.Security.Cryptography;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Core.Cleanup;

public sealed record FoundFile(string Path, long Bytes, DateTime LastWriteUtc)
{
    public string Name => System.IO.Path.GetFileName(Path);

    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>Two or more files with exactly the same contents.</summary>
public sealed record DuplicateGroup(long BytesEach, IReadOnlyList<FoundFile> Files)
{
    /// <summary>Space freed by keeping just one copy.</summary>
    public long SpareBytes => BytesEach * (Files.Count - 1);
}

public sealed record FileSearchResult(IReadOnlyList<FoundFile> Large, IReadOnlyList<DuplicateGroup> Duplicates, int FilesLookedAt);

/// <summary>
/// Finds large files and exact duplicates in the user's own folders. It only reads; deleting is
/// always the user's choice, one file at a time, and only to the Recycle Bin.
/// </summary>
public static class FileFinder
{
    public const long LargeFileBytes = 250L * 1024 * 1024;
    public const long SmallestDuplicateBytes = 1024L * 1024;
    public const int MaxLargeFiles = 100;
    public const int MaxDuplicateGroups = 100;

    // OneDrive and similar "online-only" files. Reading them downloads them, and deleting them frees nothing here.
    private const FileAttributes RecallOnOpen = (FileAttributes)0x00040000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x00400000;
    private const FileAttributes Skipped = FileAttributes.ReparsePoint | FileAttributes.Offline | FileAttributes.System | RecallOnOpen | RecallOnDataAccess;

    public static FileSearchResult Search(IEnumerable<string> roots, IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        var files = new List<FoundFile>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            progress?.Report($"Looking in {root}...");
            files.AddRange(Walk(root, cancellationToken));
        }

        var large = files.Where(f => f.Bytes >= LargeFileBytes).OrderByDescending(f => f.Bytes).Take(MaxLargeFiles).ToList();

        progress?.Report("Comparing files that might be the same...");
        var duplicates = FindDuplicates(files, cancellationToken)
            .OrderByDescending(g => g.SpareBytes)
            .Take(MaxDuplicateGroups)
            .ToList();

        return new FileSearchResult(large, duplicates, files.Count);
    }

    /// <summary>
    /// Same size first (cheap), then the first 64 KB, then the whole file, so only real duplicates
    /// are read in full.
    /// </summary>
    private static IEnumerable<DuplicateGroup> FindDuplicates(IReadOnlyList<FoundFile> files, CancellationToken cancellationToken)
    {
        foreach (var sameSize in files.Where(f => f.Bytes >= SmallestDuplicateBytes).GroupBy(f => f.Bytes).Where(g => g.Count() > 1))
        {
            foreach (var sameStart in GroupByHash(sameSize, 64 * 1024, cancellationToken))
            {
                foreach (var sameContent in GroupByHash(sameStart, null, cancellationToken))
                {
                    yield return new DuplicateGroup(sameSize.Key, sameContent.OrderBy(f => f.Path, StringComparer.OrdinalIgnoreCase).ToList());
                }
            }
        }
    }

    private static IEnumerable<List<FoundFile>> GroupByHash(IEnumerable<FoundFile> files, int? firstBytes, CancellationToken cancellationToken)
    {
        var groups = new Dictionary<string, List<FoundFile>>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Hash(file.Path, firstBytes) is not { } hash)
            {
                continue;
            }

            if (!groups.TryGetValue(hash, out var list))
            {
                groups[hash] = list = [];
            }

            list.Add(file);
        }

        return groups.Values.Where(g => g.Count > 1);
    }

    private static string? Hash(string path, int? firstBytes)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1024 * 1024);
            if (firstBytes is { } count)
            {
                var buffer = new byte[count];
                var read = stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false);
                return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
            }

            return Convert.ToHexString(SHA256.HashData(stream));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IEnumerable<FoundFile> Walk(string root, CancellationToken cancellationToken)
    {
        var start = new DirectoryInfo(root);
        if (!start.Exists || start.Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            yield break;
        }

        var pending = new Stack<DirectoryInfo>();
        pending.Push(start);
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileSystemInfo[] entries;
            try
            {
                entries = pending.Pop().GetFileSystemInfos();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                FileAttributes attributes;
                try
                {
                    attributes = entry.Attributes;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    continue;
                }

                if ((attributes & Skipped) != 0)
                {
                    continue;
                }

                if (entry is DirectoryInfo directory)
                {
                    pending.Push(directory);
                }
                else if (entry is FileInfo file)
                {
                    long length;
                    try
                    {
                        length = file.Length;
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    yield return new FoundFile(file.FullName, length, file.LastWriteTimeUtc);
                }
            }
        }
    }

    /// <summary>True if every copy in some duplicate group is selected, which would lose the file completely.</summary>
    public static DuplicateGroup? GroupWithEveryCopySelected(IEnumerable<DuplicateGroup> groups, IReadOnlySet<string> selectedPaths) =>
        groups.FirstOrDefault(g => g.Files.All(f => selectedPaths.Contains(f.Path)));

    /// <summary>The user's own folders: Desktop, Documents, Downloads, Pictures, Videos and Music.</summary>
    public static IReadOnlyList<string> UserFolders()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                string.IsNullOrEmpty(profile) ? "" : System.IO.Path.Combine(profile, "Downloads"),
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                Environment.GetFolderPath(Environment.SpecialFolder.MyVideos),
                Environment.GetFolderPath(Environment.SpecialFolder.MyMusic),
            }
            .Where(p => !string.IsNullOrEmpty(p) && Directory.Exists(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}

/// <summary>Move one file to the Recycle Bin. Only ever files inside the folders that were searched.</summary>
public sealed record RecycleFileAction(FoundFile File, IReadOnlyList<string> AllowedFolders) : PlannedAction
{
    public override string Summary => $"Move {File.Name} to the Recycle Bin ({ByteSize.Format(File.Bytes)})";

    public override string? Detail => $"From {File.Folder}";

    public override UndoSupport Undo => UndoSupport.FromRecycleBin;

    public override ProtectionResult CheckProtection() =>
        IsInside(File.Path, AllowedFolders)
            ? ProtectionResult.NotProtected
            : ProtectionResult.Because("Only files in your own folders (like Documents and Downloads) can be moved from here.");

    /// <summary>True if the path is inside one of the folders, after resolving any ".." in it.</summary>
    public static bool IsInside(string path, IEnumerable<string> folders)
    {
        string full;
        try
        {
            full = System.IO.Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return folders.Any(folder =>
        {
            var root = System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(folder)) + System.IO.Path.DirectorySeparatorChar;
            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase);
        });
    }
}
