namespace AMSpeedUp.Core.Cleanup;

public sealed record FolderMeasure(long Bytes, int Files);

/// <param name="FilesSkipped">Files that couldn't be deleted, usually because a program is using them.</param>
public sealed record CleanResult(long BytesFreed, int FilesDeleted, int FilesSkipped);

/// <summary>
/// Measures and empties a folder safely:
/// <list type="bullet">
/// <item>It never follows a link (symlink or junction) out of the folder, so nothing outside it can be touched.</item>
/// <item>It only deletes files last changed before the cut-off, so files in use right now are left alone.</item>
/// <item>The folder itself is kept. Empty folders inside it are removed.</item>
/// </list>
/// </summary>
public static class FolderCleaner
{
    public static FolderMeasure Measure(string root, DateTime olderThanUtc)
    {
        long bytes = 0;
        var files = 0;
        foreach (var file in Walk(root))
        {
            if (IsOldEnough(file, olderThanUtc))
            {
                bytes += SafeLength(file);
                files++;
            }
        }

        return new FolderMeasure(bytes, files);
    }

    public static CleanResult Clean(string root, DateTime olderThanUtc, CancellationToken cancellationToken = default)
    {
        long freed = 0;
        var deleted = 0;
        var skipped = 0;
        var folders = new List<DirectoryInfo>();

        foreach (var file in Walk(root, folders))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsOldEnough(file, olderThanUtc))
            {
                continue;
            }

            var length = SafeLength(file);
            try
            {
                if (file.IsReadOnly)
                {
                    file.IsReadOnly = false;
                }

                file.Delete();
                freed += length;
                deleted++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
            }
        }

        // Deepest first, so a folder whose sub-folders were all emptied can go too.
        foreach (var folder in folders.OrderByDescending(f => f.FullName.Length))
        {
            try
            {
                if (!folder.EnumerateFileSystemInfos().Any())
                {
                    folder.Delete();
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A program is using it. Leave it.
            }
        }

        return new CleanResult(freed, deleted, skipped);
    }

    /// <summary>Every real file under the folder, never going through links. Sub-folders are collected for tidying up.</summary>
    private static IEnumerable<FileInfo> Walk(string root, List<DirectoryInfo>? folders = null)
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
            var current = pending.Pop();
            FileSystemInfo[] entries;
            try
            {
                entries = current.GetFileSystemInfos();
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

                // A link could point anywhere, so it's never followed or deleted.
                if (attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    continue;
                }

                if (entry is DirectoryInfo directory)
                {
                    folders?.Add(directory);
                    pending.Push(directory);
                }
                else if (entry is FileInfo file)
                {
                    yield return file;
                }
            }
        }
    }

    private static bool IsOldEnough(FileInfo file, DateTime olderThanUtc)
    {
        try
        {
            return file.LastWriteTimeUtc < olderThanUtc;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static long SafeLength(FileInfo file)
    {
        try
        {
            return file.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }
}
