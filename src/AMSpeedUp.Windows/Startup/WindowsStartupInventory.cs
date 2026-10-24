using System.Diagnostics;
using System.Text.Json;
using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Startup;
using Microsoft.Win32;

namespace AMSpeedUp.Windows.Startup;

/// <summary>
/// Lists what starts at sign-in: the Run keys, the Startup folders and scheduled tasks that run
/// at logon (Windows' own tasks under \Microsoft\ are left out). Read-only.
/// </summary>
public sealed class WindowsStartupInventory(IPowerShellRunner powerShell, IAppLog log) : IStartupInventory
{
    private const string RunKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string RunKey32 = @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";

    private static readonly string[] IgnoredFiles = ["desktop.ini", "thumbs.db"];

    private const string TaskScript = """
        $result = [ordered]@{ tasks = @(); error = $null }
        try {
            $result.tasks = @(Get-ScheduledTask -ErrorAction Stop |
                Where-Object { $_.TaskPath -notlike '\Microsoft\*' -and @($_.Triggers | Where-Object { $_.CimClass.CimClassName -eq 'MSFT_TaskLogonTrigger' }).Count -gt 0 } |
                ForEach-Object {
                    $action = @($_.Actions | Where-Object { $_.Execute }) | Select-Object -First 1
                    [ordered]@{
                        name = [string]$_.TaskName
                        path = [string]$_.TaskPath
                        state = [string]$_.State
                        execute = if ($action) { [string]$action.Execute } else { $null }
                        arguments = if ($action) { [string]$action.Arguments } else { $null }
                    }
                })
        } catch { $result.error = $_.Exception.Message }
        $result | ConvertTo-Json -Compress -Depth 4
        """;

    public async Task<StartupScanResult> ScanAsync(CancellationToken cancellationToken = default)
    {
        log.Info("Listing startup programs.");
        var items = new List<StartupItem>();
        var problems = new List<string>();

        var tasks = ScanTasksAsync(cancellationToken);
        await Task.Run(() =>
        {
            ReadRunKey(RegistryHive.CurrentUser, RunKey, StartupSource.RunCurrentUser, items, problems);
            ReadRunKey(RegistryHive.LocalMachine, RunKey, StartupSource.RunAllUsers, items, problems);
            ReadRunKey(RegistryHive.LocalMachine, RunKey32, StartupSource.RunAllUsers32, items, problems);
            ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.StartupFolderCurrentUser, items, problems);
            ReadFolder(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.StartupFolderAllUsers, items, problems);
        }, cancellationToken).ConfigureAwait(false);

        var (taskItems, taskProblems) = await tasks.ConfigureAwait(false);
        items.AddRange(taskItems);
        problems.AddRange(taskProblems);

        foreach (var problem in problems)
        {
            log.Warning("Startup scan problem: " + problem);
        }

        log.Info($"Found {items.Count} startup item(s), {items.Count(i => !i.IsEnabled)} of them switched off.");
        return new StartupScanResult(items, problems);
    }

    /// <summary>Reads the on/off state Task Manager stores. No value means on.</summary>
    public static bool ReadApproved(StartupItem item)
    {
        if (StartupApproved.Location(item) is not { } location)
        {
            return item.IsEnabled;
        }

        using var root = RegistryKey.OpenBaseKey(location.AllUsers ? RegistryHive.LocalMachine : RegistryHive.CurrentUser, RegistryView.Registry64);
        using var key = root.OpenSubKey(location.SubKey);
        return StartupApproved.IsEnabled(key?.GetValue(location.ValueName) as byte[]);
    }

    private static void ReadRunKey(RegistryHive hive, string path, StartupSource source, List<StartupItem> items, List<string> problems)
    {
        try
        {
            using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var key = root.OpenSubKey(path);
            if (key is null)
            {
                return;
            }

            foreach (var name in key.GetValueNames().Where(n => !string.IsNullOrEmpty(n)))
            {
                var command = key.GetValue(name) as string;
                var program = ProgramFromCommand(command);
                var item = new StartupItem
                {
                    Source = source,
                    Name = name,
                    Command = command,
                    ProgramPath = program,
                    Publisher = PublisherOf(program),
                    IsEnabled = true,
                };
                items.Add(item with { IsEnabled = ReadApproved(item) });
            }
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
        {
            problems.Add($"Couldn't read {hive}\\{path}: {ex.Message}");
        }
    }

    private static void ReadFolder(string folder, StartupSource source, List<StartupItem> items, List<string> problems)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            return;
        }

        try
        {
            foreach (var file in Directory.EnumerateFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (IgnoredFiles.Contains(name, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                var program = name.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ? ShortcutTarget(file) ?? file : file;
                var item = new StartupItem
                {
                    Source = source,
                    Name = name,
                    Command = program,
                    ProgramPath = program,
                    Publisher = PublisherOf(program),
                    IsEnabled = true,
                };
                items.Add(item with { IsEnabled = ReadApproved(item) });
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            problems.Add($"Couldn't read the Startup folder {folder}: {ex.Message}");
        }
    }

    private async Task<(IReadOnlyList<StartupItem>, IReadOnlyList<string>)> ScanTasksAsync(CancellationToken cancellationToken)
    {
        var run = await powerShell.RunAsync(TaskScript, "Listing scheduled tasks that run at sign-in", TimeSpan.FromMinutes(2), cancellationToken).ConfigureAwait(false);
        var json = PowerShellOutput.LastJsonLine(run.Output);
        if (json is null)
        {
            return ([], [run.TimedOut ? "Listing scheduled tasks took too long." : "Couldn't list scheduled tasks. " + run.Errors]);
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var problems = new List<string>();
            if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
            {
                problems.Add("Couldn't list scheduled tasks: " + error.GetString());
            }

            var items = new List<StartupItem>();
            if (root.TryGetProperty("tasks", out var tasks))
            {
                var list = tasks.ValueKind switch
                {
                    JsonValueKind.Array => tasks.EnumerateArray().ToList(),
                    JsonValueKind.Object => [tasks],
                    _ => [],
                };

                foreach (var task in list)
                {
                    var name = task.GetProperty("name").GetString();
                    if (string.IsNullOrEmpty(name))
                    {
                        continue;
                    }

                    var execute = task.TryGetProperty("execute", out var e) && e.ValueKind == JsonValueKind.String ? e.GetString() : null;
                    var arguments = task.TryGetProperty("arguments", out var a) && a.ValueKind == JsonValueKind.String ? a.GetString() : null;
                    var program = execute is null ? null : Environment.ExpandEnvironmentVariables(execute.Trim('"'));
                    items.Add(new StartupItem
                    {
                        Source = StartupSource.ScheduledTask,
                        Name = name,
                        TaskPath = task.GetProperty("path").GetString(),
                        Command = $"{execute} {arguments}".Trim(),
                        ProgramPath = program,
                        Publisher = PublisherOf(program),
                        IsEnabled = !string.Equals(task.GetProperty("state").GetString(), "Disabled", StringComparison.OrdinalIgnoreCase),
                    });
                }
            }

            return (items, problems);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return ([], ["Couldn't read the scheduled task list: " + ex.Message]);
        }
    }

    private static string? ProgramFromCommand(string? command)
    {
        var split = CommandLineSplitter.Split(command);
        return split is null ? null : Environment.ExpandEnvironmentVariables(split.FileName);
    }

    private static string? PublisherOf(string? path)
    {
        try
        {
            if (path is null || !File.Exists(path))
            {
                return null;
            }

            var company = FileVersionInfo.GetVersionInfo(path).CompanyName?.Trim();
            return string.IsNullOrEmpty(company) ? null : company;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    private static string? ShortcutTarget(string shortcut)
    {
        try
        {
            var type = Type.GetTypeFromProgID("WScript.Shell");
            if (type is null)
            {
                return null;
            }

            dynamic shell = Activator.CreateInstance(type)!;
            try
            {
                dynamic link = shell.CreateShortcut(shortcut);
                string target = link.TargetPath;
                return string.IsNullOrWhiteSpace(target) ? null : target;
            }
            finally
            {
                System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);
            }
        }
        catch (Exception)
        {
            // A broken or unusual shortcut. The item is still listed, just without its program.
            return null;
        }
    }
}
