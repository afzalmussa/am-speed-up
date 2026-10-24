using System.Diagnostics;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;

namespace AMSpeedUp.Windows.Platform;

public sealed class WindowsShellLauncher(IAppLog log) : IShellLauncher
{
    public bool OpenUrl(string url)
    {
        if (!AppSettings.IsUsableUrl(url))
        {
            log.Warning($"Refused to open '{url}' because it is not a secure (https) web address.");
            return false;
        }

        return Start(new ProcessStartInfo(url) { UseShellExecute = true }, $"web page {url}");
    }

    public bool OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            log.Warning($"Could not open '{path}' because the file does not exist.");
            return false;
        }

        return Start(new ProcessStartInfo(path) { UseShellExecute = true }, $"file {path}");
    }

    public bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Could not create folder '{path}'.", ex);
            return false;
        }

        var explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        var info = new ProcessStartInfo(explorer) { UseShellExecute = false };
        info.ArgumentList.Add(path);
        return Start(info, $"folder {path}");
    }

    public bool OpenWindowsPage(WindowsPage page)
    {
        var info = page switch
        {
            WindowsPage.VirusProtection => new ProcessStartInfo("windowsdefender://threat") { UseShellExecute = true },
            WindowsPage.SystemProtection => new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "SystemPropertiesProtection.exe"))
            {
                UseShellExecute = false,
            },
            WindowsPage.SystemRestore => new ProcessStartInfo(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "rstrui.exe"))
            {
                UseShellExecute = false,
            },
            WindowsPage.RecycleBin => RecycleBinInfo(),
            _ => throw new ArgumentOutOfRangeException(nameof(page), page, null),
        };

        return Start(info, page switch
        {
            WindowsPage.VirusProtection => "Windows Security",
            WindowsPage.SystemProtection => "System Protection settings",
            WindowsPage.RecycleBin => "the Recycle Bin",
            _ => "System Restore",
        });
    }

    private static ProcessStartInfo RecycleBinInfo()
    {
        var info = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe")) { UseShellExecute = false };
        info.ArgumentList.Add("shell:RecycleBinFolder");
        return info;
    }

    public bool OpenStoreSearch(string appName)
    {
        var uri = "ms-windows-store://search/?query=" + Uri.EscapeDataString(appName.Trim());
        return Start(new ProcessStartInfo(uri) { UseShellExecute = true }, $"the Microsoft Store search for {appName}");
    }

    private bool Start(ProcessStartInfo info, string what)
    {
        try
        {
            using var _ = Process.Start(info);
            log.Info($"Opened {what}.");
            return true;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException)
        {
            log.Error($"Could not open {what}.", ex);
            return false;
        }
    }
}
