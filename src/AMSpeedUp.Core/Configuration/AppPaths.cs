namespace AMSpeedUp.Core.Configuration;

/// <summary>Where the app keeps its files: %ProgramData%\AMSoftware\SpeedUp on Windows.</summary>
public static class AppPaths
{
    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AMSoftware", "SpeedUp");

    public static string Logs => Path.Combine(DataRoot, "logs");

    public static string UndoHistory => Path.Combine(DataRoot, "undo-history.json");

    public static string Licence => Path.Combine(DataRoot, "licence.dat");

    /// <summary>Where a copy of the app is kept for the monthly tune-up, so it still works if the download is deleted.</summary>
    public static string InstalledFolder { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "AM Software", "SpeedUp");

    public static string InstalledExe => Path.Combine(InstalledFolder, "AMSpeedUp.exe");
}
