namespace AMSpeedUp.Core.Platform;

public interface ISystemInfo
{
    /// <summary>Friendly Windows name and build, for the log. For example "Windows 11 (build 22631)".</summary>
    string WindowsDescription { get; }

    bool IsRunningAsAdministrator { get; }
}
