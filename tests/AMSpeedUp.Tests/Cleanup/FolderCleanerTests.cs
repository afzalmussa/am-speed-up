using AMSpeedUp.Core.Cleanup;

namespace AMSpeedUp.Tests.Cleanup;

public sealed class FolderCleanerTests : IDisposable
{
    private static readonly DateTime Now = DateTime.UtcNow;
    private static readonly DateTime Cutoff = Now.AddDays(-1);

    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    private string File(string relative, int size, DateTime lastWrite)
    {
        var path = _temp.Combine(relative.Split('/'));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        System.IO.File.WriteAllBytes(path, new byte[size]);
        System.IO.File.SetLastWriteTimeUtc(path, lastWrite);
        return path;
    }

    [Fact]
    public void Measures_only_files_older_than_the_cut_off()
    {
        File("target/old.tmp", 100, Now.AddDays(-3));
        File("target/sub/old2.tmp", 50, Now.AddDays(-2));
        File("target/new.tmp", 1000, Now);

        var measure = FolderCleaner.Measure(_temp.Combine("target"), Cutoff);

        Assert.Equal(new FolderMeasure(150, 2), measure);
    }

    [Fact]
    public void Cleans_old_files_keeps_new_ones_and_keeps_the_folder_itself()
    {
        var old = File("target/old.tmp", 100, Now.AddDays(-3));
        var nested = File("target/sub/deeper/old2.tmp", 50, Now.AddDays(-2));
        var fresh = File("target/new.tmp", 1000, Now);

        var result = FolderCleaner.Clean(_temp.Combine("target"), Cutoff);

        Assert.Equal(new CleanResult(150, 2, 0), result);
        Assert.False(System.IO.File.Exists(old));
        Assert.False(System.IO.File.Exists(nested));
        Assert.True(System.IO.File.Exists(fresh));
        Assert.True(Directory.Exists(_temp.Combine("target")));
        Assert.False(Directory.Exists(_temp.Combine("target", "sub")));
    }

    [Fact]
    public void Folders_that_still_hold_new_files_are_kept()
    {
        File("target/sub/old.tmp", 10, Now.AddDays(-3));
        var fresh = File("target/sub/new.tmp", 10, Now);

        FolderCleaner.Clean(_temp.Combine("target"), Cutoff);

        Assert.True(System.IO.File.Exists(fresh));
    }

    [Fact]
    public void A_cut_off_in_the_future_clears_everything()
    {
        File("cache/a", 10, Now);
        File("cache/b/c", 10, Now);

        var result = FolderCleaner.Clean(_temp.Combine("cache"), DateTime.MaxValue);

        Assert.Equal(2, result.FilesDeleted);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Combine("cache")));
    }

    [Fact]
    public void Read_only_files_are_still_cleared()
    {
        var path = File("target/locked.tmp", 10, Now.AddDays(-3));
        new FileInfo(path).IsReadOnly = true;

        var result = FolderCleaner.Clean(_temp.Combine("target"), Cutoff);

        Assert.Equal(1, result.FilesDeleted);
        Assert.False(System.IO.File.Exists(path));
    }

    [Fact]
    public void A_link_inside_the_folder_is_never_followed()
    {
        // Something precious outside the folder, and a link to it from inside.
        var precious = File("precious/family-photo.jpg", 500, Now.AddDays(-30));
        File("target/old.tmp", 10, Now.AddDays(-3));
        var link = _temp.Combine("target", "link-to-precious");
        Directory.CreateSymbolicLink(link, _temp.Combine("precious"));

        var measure = FolderCleaner.Measure(_temp.Combine("target"), Cutoff);
        var result = FolderCleaner.Clean(_temp.Combine("target"), Cutoff);

        Assert.Equal(10, measure.Bytes);
        Assert.Equal(1, result.FilesDeleted);
        Assert.True(System.IO.File.Exists(precious));
        Assert.NotNull(new DirectoryInfo(link).LinkTarget);
    }

    [Fact]
    public void A_folder_that_is_itself_a_link_is_refused()
    {
        var precious = File("precious/family-photo.jpg", 500, Now.AddDays(-30));
        var linked = _temp.Combine("linked-target");
        Directory.CreateSymbolicLink(linked, _temp.Combine("precious"));

        var result = FolderCleaner.Clean(linked, DateTime.MaxValue);

        Assert.Equal(0, result.FilesDeleted);
        Assert.True(System.IO.File.Exists(precious));
    }

    [Fact]
    public void A_linked_file_is_not_deleted_or_counted()
    {
        var precious = File("precious/letter.docx", 500, Now.AddDays(-30));
        var link = _temp.Combine("target", "letter-shortcut.docx");
        Directory.CreateDirectory(_temp.Combine("target"));
        System.IO.File.CreateSymbolicLink(link, precious);

        var result = FolderCleaner.Clean(_temp.Combine("target"), DateTime.MaxValue);

        Assert.Equal(0, result.FilesDeleted);
        Assert.True(System.IO.File.Exists(precious));
    }

    [Fact]
    public void A_missing_folder_measures_as_empty_and_cleans_nothing()
    {
        var missing = _temp.Combine("does-not-exist");

        Assert.Equal(new FolderMeasure(0, 0), FolderCleaner.Measure(missing, Cutoff));
        Assert.Equal(new CleanResult(0, 0, 0), FolderCleaner.Clean(missing, Cutoff));
    }

    [Fact]
    public void Cancelling_stops_the_clean()
    {
        File("target/a.tmp", 10, Now.AddDays(-3));
        using var cancel = new CancellationTokenSource();
        cancel.Cancel();

        Assert.Throws<OperationCanceledException>(() => FolderCleaner.Clean(_temp.Combine("target"), Cutoff, cancel.Token));
    }
}
