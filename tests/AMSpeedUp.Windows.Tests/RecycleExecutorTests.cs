using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Cleanup;
using AMSpeedUp.Windows.Cleanup;

namespace AMSpeedUp.Windows.Tests;

/// <summary>Creates its own small file in a temporary folder and moves it to the Recycle Bin.</summary>
public sealed class RecycleExecutorTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "amspeedup-recycle-test-" + Guid.NewGuid().ToString("N"));

    public RecycleExecutorTests() => Directory.CreateDirectory(_folder);

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    [Fact]
    public async Task A_file_in_an_allowed_folder_is_moved_out_of_its_folder()
    {
        var path = Path.Combine(_folder, "test.txt");
        await File.WriteAllTextAsync(path, "AM Speed Up test file");
        var action = new RecycleFileAction(new FoundFile(path, 21, DateTime.UtcNow), [_folder]);

        var outcome = await new RecycleExecutor(new ListLog()).RunAsync(action, CancellationToken.None);

        Assert.Equal(OutcomeStatus.Succeeded, outcome.Status);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task A_file_outside_the_allowed_folders_is_refused()
    {
        var path = Path.Combine(_folder, "test.txt");
        await File.WriteAllTextAsync(path, "x");
        var action = new RecycleFileAction(new FoundFile(path, 1, DateTime.UtcNow), [Path.Combine(_folder, "somewhere-else")]);

        var outcome = await new RecycleExecutor(new ListLog()).RunAsync(action, CancellationToken.None);

        Assert.Equal(OutcomeStatus.Skipped, outcome.Status);
        Assert.True(File.Exists(path));
    }
}
