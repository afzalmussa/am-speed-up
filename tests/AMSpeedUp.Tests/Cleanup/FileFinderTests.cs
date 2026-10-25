using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;

namespace AMSpeedUp.Tests.Cleanup;

public sealed class FileFinderTests : IDisposable
{
    private const int Mb = 1024 * 1024;

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string Write(string relative, byte[] content)
    {
        var path = _temp.Combine(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] Content(int size, byte seed, int? changeAt = null)
    {
        var bytes = new byte[size];
        for (var i = 0; i < size; i++)
        {
            bytes[i] = (byte)(seed + i % 251);
        }

        if (changeAt is { } at)
        {
            bytes[at] ^= 0xFF;
        }

        return bytes;
    }

    private string Sparse(string relative, long size)
    {
        var path = _temp.Combine(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        stream.SetLength(size);
        return path;
    }

    [Fact]
    public void Large_files_are_found_biggest_first()
    {
        Sparse("Videos/holiday.mp4", 900L * Mb);
        Sparse("Downloads/windows.iso", 300L * Mb);
        Sparse("Downloads/small.zip", 10L * Mb);

        var result = FileFinder.Search([_temp.Path]);

        Assert.Equal(["holiday.mp4", "windows.iso"], result.Large.Select(f => f.Name));
        Assert.Equal(900L * Mb, result.Large[0].Bytes);
    }

    [Fact]
    public void Exact_duplicates_are_grouped_and_lookalikes_are_not()
    {
        var a = Write("Pictures/beach.jpg", Content(2 * Mb, 1));
        var b = Write("Downloads/beach (1).jpg", Content(2 * Mb, 1));
        var c = Write("Desktop/beach copy.jpg", Content(2 * Mb, 1));
        Write("Pictures/sunset.jpg", Content(2 * Mb, 7));
        Write("Pictures/beach-edited.jpg", Content(2 * Mb, 1, changeAt: (2 * Mb) - 10));

        var result = FileFinder.Search([_temp.Path]);

        var group = Assert.Single(result.Duplicates);
        Assert.Equal(new[] { a, b, c }.Order(StringComparer.OrdinalIgnoreCase), group.Files.Select(f => f.Path));
        Assert.Equal(2L * Mb, group.BytesEach);
        Assert.Equal(4L * Mb, group.SpareBytes);
    }

    [Fact]
    public void Small_files_are_not_compared()
    {
        Write("Documents/a.txt", Content(1000, 3));
        Write("Documents/b.txt", Content(1000, 3));

        Assert.Empty(FileFinder.Search([_temp.Path]).Duplicates);
    }

    [Fact]
    public void Links_are_never_followed()
    {
        Sparse("elsewhere/huge.bin", 500L * Mb);
        Directory.CreateDirectory(_temp.Combine("Documents"));
        Directory.CreateSymbolicLink(_temp.Combine("Documents", "link"), _temp.Combine("elsewhere"));

        var result = FileFinder.Search([_temp.Combine("Documents")]);

        Assert.Empty(result.Large);
        Assert.Equal(0, result.FilesLookedAt);
    }

    [Fact]
    public void Missing_folders_are_skipped_quietly()
    {
        var result = FileFinder.Search([_temp.Combine("not-there")]);

        Assert.Empty(result.Large);
        Assert.Empty(result.Duplicates);
    }

    [Fact]
    public void A_duplicate_group_with_every_copy_selected_is_spotted()
    {
        var one = new FoundFile("/a/x.jpg", 10, DateTime.UtcNow);
        var two = new FoundFile("/b/x.jpg", 10, DateTime.UtcNow);
        var groups = new[] { new DuplicateGroup(10, [one, two]) };

        Assert.Null(FileFinder.GroupWithEveryCopySelected(groups, new HashSet<string> { "/a/x.jpg" }));
        Assert.NotNull(FileFinder.GroupWithEveryCopySelected(groups, new HashSet<string> { "/a/x.jpg", "/b/x.jpg" }));
    }

    [Fact]
    public void Moving_to_the_Recycle_Bin_is_described_and_can_be_undone_from_there()
    {
        var file = new FoundFile(_temp.Combine("Videos", "holiday.mp4"), 900L * Mb, DateTime.UtcNow);
        var action = new RecycleFileAction(file, [_temp.Path]);

        Assert.Equal("Move holiday.mp4 to the Recycle Bin (900 MB)", action.Summary);
        Assert.Equal(UndoSupport.FromRecycleBin, action.Undo);
        Assert.False(action.NeedsRestorePoint);
        Assert.Contains("Recycle Bin", ActionPlan.Create("t", [action]).CreatePreview(false).Sections.Single().UndoNote);
    }

    [Theory]
    [InlineData("Videos/holiday.mp4", true)]
    [InlineData("Videos/../Videos/holiday.mp4", true)]
    [InlineData("../outside.mp4", false)]
    [InlineData("Videos/../../outside.mp4", false)]
    public void Only_files_inside_the_searched_folders_can_be_moved(string relative, bool allowed)
    {
        var allowedFolder = _temp.Combine("user");
        var path = Path.Combine(allowedFolder, relative.Replace('/', Path.DirectorySeparatorChar));
        var action = new RecycleFileAction(new FoundFile(path, 1, DateTime.UtcNow), [allowedFolder]);

        Assert.Equal(!allowed, action.CheckProtection().IsProtected);
    }

    [Fact]
    public void A_folder_with_a_similar_name_does_not_count_as_inside()
    {
        var action = new RecycleFileAction(new FoundFile(_temp.Combine("Documents-old", "x.doc"), 1, DateTime.UtcNow), [_temp.Combine("Documents")]);

        Assert.True(action.CheckProtection().IsProtected);
    }
}
