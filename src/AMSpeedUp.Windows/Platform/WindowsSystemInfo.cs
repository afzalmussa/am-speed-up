using System.Security.Principal;
using AMSpeedUp.Core.Platform;
using Microsoft.Win32;

namespace AMSpeedUp.Windows.Platform;

public sealed class WindowsSystemInfo : ISystemInfo
{
    private const string CurrentVersionKey = @"SOFTWARE\Microsoft\Windows NT\CurrentVersion";

    public string WindowsDescription
    {
        get
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(CurrentVersionKey);
                return WindowsVersionText.Describe(
                    key?.GetValue("ProductName") as string,
                    key?.GetValue("DisplayVersion") as string,
                    key?.GetValue("CurrentBuild") as string);
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException)
            {
                return Environment.OSVersion.VersionString;
            }
        }
    }

    public bool IsRunningAsAdministrator
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }
}
