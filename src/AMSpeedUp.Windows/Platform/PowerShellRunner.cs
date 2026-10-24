using System.Diagnostics;
using System.Text;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;

namespace AMSpeedUp.Windows.Platform;

/// <summary>
/// Runs scripts in Windows PowerShell 5.1, which ships with every copy of Windows 10 and 11 and
/// has the commands the app needs (Get-AppxPackage, Get-MpComputerStatus, Checkpoint-Computer).
/// </summary>
public sealed class PowerShellRunner(IAppLog log) : IPowerShellRunner
{
    private const int MaxLoggedCharacters = 4000;

    // Keeps progress bars out of the output and makes sure names with accents come back intact.
    private const string Preamble =
        "$ProgressPreference = 'SilentlyContinue'\n" +
        "[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding $false\n";

    public static string ExecutablePath { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe");

    public async Task<PowerShellResult> RunAsync(string script, string purpose, TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        // Sent encoded so nothing in the script is ever re-interpreted by the command line.
        var encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(Preamble + script));
        var info = new ProcessStartInfo(ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        // If this app was started from PowerShell 7, PSModulePath points at PowerShell 7's modules,
        // which Windows PowerShell can't load (Get-AuthenticodeSignature fails, for example).
        // Without it, Windows PowerShell uses its own standard module folders.
        info.Environment.Remove("PSModulePath");

        foreach (var argument in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-OutputFormat", "Text", "-EncodedCommand", encoded })
        {
            info.ArgumentList.Add(argument);
        }

        log.Info($"PowerShell: {purpose}.");
        var stopwatch = Stopwatch.StartNew();

        using var process = new Process { StartInfo = info };
        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            log.Error($"PowerShell could not be started for: {purpose}.", ex);
            return new PowerShellResult(-1, "", ex.Message, TimedOut: false);
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errorTask = process.StandardError.ReadToEndAsync(CancellationToken.None);

        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        var timedOut = false;
        try
        {
            await process.WaitForExitAsync(limit.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            if (cancellationToken.IsCancellationRequested)
            {
                log.Warning($"PowerShell stopped because the user cancelled: {purpose}.");
                throw;
            }

            timedOut = true;
        }

        var output = await outputTask.ConfigureAwait(false);
        var errors = PowerShellOutput.CleanErrors(await errorTask.ConfigureAwait(false));
        var exitCode = timedOut ? -1 : process.ExitCode;
        var result = new PowerShellResult(exitCode, output, errors, timedOut);

        var seconds = stopwatch.Elapsed.TotalSeconds.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        if (timedOut)
        {
            log.Error($"PowerShell took longer than {timeout.TotalSeconds:0} seconds and was stopped: {purpose}.");
        }
        else if (!result.Succeeded)
        {
            log.Warning($"PowerShell finished with exit code {exitCode} after {seconds}s: {purpose}.");
        }
        else
        {
            log.Info($"PowerShell finished after {seconds}s: {purpose}.");
        }

        if (output.Trim().Length > 0)
        {
            log.Info($"PowerShell output: {Shorten(output.Trim())}");
        }

        if (errors.Length > 0)
        {
            log.Warning($"PowerShell errors: {Shorten(errors)}");
        }

        return result;
    }

    private static string Shorten(string text) =>
        text.Length <= MaxLoggedCharacters ? text : text[..MaxLoggedCharacters] + $" ... ({text.Length - MaxLoggedCharacters} more characters)";

    private void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            log.Warning("Couldn't stop a PowerShell process that ran too long.", ex);
        }
    }
}
