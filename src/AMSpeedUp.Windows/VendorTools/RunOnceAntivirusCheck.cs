using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.VendorTools;
using Microsoft.Win32;

namespace AMSpeedUp.Windows.VendorTools;

/// <summary>
/// Uses Windows' RunOnce list to open AM Speed Up once at the next sign-in, so it can check
/// Microsoft Defender after a removal tool restarts the PC. Windows deletes the entry after use.
/// </summary>
public sealed class RunOnceAntivirusCheck(IAppLog log) : IAntivirusCheckAfterRestart
{
    private const string RunOnceKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\RunOnce";
    private const string ValueName = "AMSpeedUpAntivirusCheck";

    public void Schedule()
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            log.Warning("Couldn't schedule the antivirus check after restart: the app's own location is unknown.");
            return;
        }

        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RunOnceKey, writable: true);
            key.SetValue(ValueName, $"\"{exe}\" {AppOptions.CheckAntivirusFlag}");
            log.Info("Scheduled an antivirus check for after the next restart.");
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            log.Warning("Couldn't schedule the antivirus check after restart.", ex);
        }
    }

    public void Cancel()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RunOnceKey, writable: true);
            if (key?.GetValue(ValueName) is not null)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                log.Info("The PC wasn't restarted, so the antivirus check after restart isn't needed.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            log.Warning("Couldn't remove the scheduled antivirus check.", ex);
        }
    }
}
