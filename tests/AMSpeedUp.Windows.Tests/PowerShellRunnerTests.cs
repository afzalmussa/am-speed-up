using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Windows.Platform;

namespace AMSpeedUp.Windows.Tests;

public class PowerShellRunnerTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromMinutes(1);

    private readonly ListLog _log = new();

    private PowerShellRunner Runner => new(_log);

    [Fact]
    public async Task Runs_a_script_and_returns_its_output()
    {
        var result = await Runner.RunAsync("Write-Output 'hello'", "test", Generous);

        Assert.True(result.Succeeded);
        Assert.Equal("hello", result.Output.Trim());
        Assert.Contains(_log.Lines, l => l.Contains("PowerShell: test."));
    }

    [Fact]
    public async Task Reports_the_exit_code()
    {
        var result = await Runner.RunAsync("exit 3", "exit code test", Generous);

        Assert.Equal(3, result.ExitCode);
        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Errors_come_back_as_readable_text()
    {
        var result = await Runner.RunAsync("Write-Error 'Something broke'; exit 1", "error test", Generous);

        Assert.Contains("Something broke", result.Errors);
        Assert.DoesNotContain("CLIXML", result.Errors);
    }

    [Fact]
    public async Task Scripts_that_run_too_long_are_stopped()
    {
        var result = await Runner.RunAsync("Start-Sleep -Seconds 60", "timeout test", TimeSpan.FromSeconds(3));

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.Contains(_log.Lines, l => l.Contains("took longer than"));
    }

    [Fact]
    public async Task Cancelling_stops_the_script()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Runner.RunAsync("Start-Sleep -Seconds 60", "cancel test", Generous, cancel.Token));
    }

    // Values such as app names are inserted with PowerShellQuoting. Whatever they contain,
    // they must come back as plain text and never run as code.
    [Theory]
    [InlineData("Candy Crush Saga")]
    [InlineData("It's a trap")]
    [InlineData("x'; Write-Output 'INJECTED'; '")]
    [InlineData("x’; Write-Output 'INJECTED'; ‘")]
    [InlineData("$(Write-Output INJECTED)")]
    [InlineData("$env:COMPUTERNAME")]
    [InlineData("back`ntick")]
    [InlineData("Café Gölge ✓ 中文")]
    public async Task Quoted_values_come_back_exactly_and_are_never_run(string value)
    {
        var result = await Runner.RunAsync($"$value = {PowerShellQuoting.Literal(value)}\n[Console]::Out.Write($value)", "quoting test", Generous);

        // An exact match proves nothing inside the value ran: injected code would change the output.
        Assert.True(result.Succeeded, result.Errors);
        Assert.Equal(value, result.Output);
    }
}
