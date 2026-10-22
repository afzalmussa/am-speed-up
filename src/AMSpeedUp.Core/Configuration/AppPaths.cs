namespace AMSpeedUp.Core.Configuration;

/// <summary>Where the app keeps its files: %ProgramData%\AMSoftware\SpeedUp on Windows.</summary>
public static class AppPaths
{
    public static string DataRoot { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AMSoftware", "SpeedUp");

    public static string Logs => Path.Combine(DataRoot, "logs");

    public static string UndoHistory => Path.Combine(DataRoot, "undo-history.json");
}
