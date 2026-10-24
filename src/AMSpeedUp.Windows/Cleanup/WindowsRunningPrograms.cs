using System.Diagnostics;
using AMSpeedUp.Core.Cleanup;

namespace AMSpeedUp.Windows.Cleanup;

public sealed class WindowsRunningPrograms : IRunningPrograms
{
    public bool IsRunning(string processName)
    {
        var processes = Process.GetProcessesByName(processName);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var process in processes)
            {
                process.Dispose();
            }
        }
    }
}
