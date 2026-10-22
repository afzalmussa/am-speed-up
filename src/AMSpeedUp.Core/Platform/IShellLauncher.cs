namespace AMSpeedUp.Core.Platform;

/// <summary>Opens web pages, files and folders with whatever Windows normally uses for them.</summary>
public interface IShellLauncher
{
    /// <summary>Opens an https address in the default browser. Returns false if it couldn't.</summary>
    bool OpenUrl(string url);

    /// <summary>Opens a file with its default program (a log file opens in Notepad, for example).</summary>
    bool OpenFile(string path);

    bool OpenFolder(string path);
}
