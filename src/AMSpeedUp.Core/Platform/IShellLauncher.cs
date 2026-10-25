namespace AMSpeedUp.Core.Platform;

/// <summary>Windows settings pages the app can open for the user. A fixed list, so nothing else can be launched this way.</summary>
public enum WindowsPage
{
    /// <summary>Windows Security, on the "Virus and threat protection" page.</summary>
    VirusProtection,

    /// <summary>The "System Protection" tab where System Restore is turned on.</summary>
    SystemProtection,

    /// <summary>System Restore itself, for winding the PC back to a restore point.</summary>
    SystemRestore,

    /// <summary>The Recycle Bin in File Explorer, to look inside before emptying it.</summary>
    RecycleBin,
}

/// <summary>Opens web pages, files and folders with whatever Windows normally uses for them.</summary>
public interface IShellLauncher
{
    /// <summary>Opens an https address in the default browser. Returns false if it couldn't.</summary>
    bool OpenUrl(string url);

    /// <summary>Opens a file with its default program (a log file opens in Notepad, for example).</summary>
    bool OpenFile(string path);

    bool OpenFolder(string path);

    bool OpenWindowsPage(WindowsPage page);

    /// <summary>Opens the Microsoft Store app, searching for an app by name, so it can be reinstalled.</summary>
    bool OpenStoreSearch(string appName);

    /// <summary>Opens File Explorer with the file selected.</summary>
    bool ShowInFolder(string filePath);
}
