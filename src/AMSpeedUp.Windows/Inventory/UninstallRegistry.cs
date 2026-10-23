using AMSpeedUp.Core.Inventory;
using Microsoft.Win32;

namespace AMSpeedUp.Windows.Inventory;

/// <summary>Reads the Uninstall keys that "Installed apps" uses. Read-only.</summary>
public static class UninstallRegistry
{
    private static readonly (RegistryHive Hive, string Prefix, string Path)[] Locations =
    [
        (RegistryHive.LocalMachine, "HKLM", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.LocalMachine, "HKLM", @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"),
        (RegistryHive.CurrentUser, "HKCU", @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
    ];

    public static (IReadOnlyList<UninstallEntry> Entries, IReadOnlyList<string> Problems) ReadAll()
    {
        var entries = new List<UninstallEntry>();
        var problems = new List<string>();

        foreach (var (hive, prefix, path) in Locations)
        {
            try
            {
                // The 64-bit view sees both lists: WOW6432Node is named explicitly in the path.
                using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
                using var uninstall = root.OpenSubKey(path);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    try
                    {
                        using var key = uninstall.OpenSubKey(name);
                        if (key is null)
                        {
                            continue;
                        }

                        entries.Add(new UninstallEntry
                        {
                            KeyName = name,
                            KeyPath = $@"{prefix}\{path}\{name}",
                            DisplayName = key.GetValue("DisplayName") as string,
                            Publisher = key.GetValue("Publisher") as string,
                            DisplayVersion = key.GetValue("DisplayVersion") as string,
                            UninstallString = key.GetValue("UninstallString") as string,
                            QuietUninstallString = key.GetValue("QuietUninstallString") as string,
                            SystemComponent = key.GetValue("SystemComponent") as int?,
                            WindowsInstaller = key.GetValue("WindowsInstaller") as int?,
                            ParentKeyName = key.GetValue("ParentKeyName") as string,
                            ReleaseType = key.GetValue("ReleaseType") as string,
                        });
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
                    {
                        problems.Add($@"Couldn't read {prefix}\{path}\{name}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                problems.Add($@"Couldn't read {prefix}\{path}: {ex.Message}");
            }
        }

        return (entries, problems);
    }

    /// <summary>True while the program's uninstall key still exists, meaning it's still installed.</summary>
    public static bool StillInstalled(string keyPath)
    {
        var slash = keyPath.IndexOf('\\');
        if (slash < 0)
        {
            return false;
        }

        var hive = keyPath[..slash] switch
        {
            "HKLM" => RegistryHive.LocalMachine,
            "HKCU" => RegistryHive.CurrentUser,
            _ => throw new ArgumentException($"Unknown registry hive in '{keyPath}'.", nameof(keyPath)),
        };

        using var root = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var key = root.OpenSubKey(keyPath[(slash + 1)..]);
        return key is not null;
    }
}
