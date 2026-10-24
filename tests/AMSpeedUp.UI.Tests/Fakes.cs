using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Core.VendorTools;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Services;

namespace AMSpeedUp.UI.Tests;

internal sealed class FakeLog : IAppLog
{
    public List<(LogLevel Level, string Message)> Entries { get; } = [];

    public string LogDirectory { get; init; } = @"C:\ProgramData\AMSoftware\SpeedUp\logs";

    public string CurrentLogFile => System.IO.Path.Combine(LogDirectory, "speedup-2026-10-22.log");

    public void Write(LogLevel level, string message, Exception? exception = null) => Entries.Add((level, message));
}

internal sealed class FakeShell : IShellLauncher
{
    public bool UrlsWork { get; set; } = true;
    public bool FilesWork { get; set; } = true;
    public bool FoldersWork { get; set; } = true;

    public List<string> OpenedUrls { get; } = [];
    public List<string> OpenedFiles { get; } = [];
    public List<string> OpenedFolders { get; } = [];

    public bool OpenUrl(string url)
    {
        OpenedUrls.Add(url);
        return UrlsWork;
    }

    public bool OpenFile(string path)
    {
        OpenedFiles.Add(path);
        return FilesWork;
    }

    public bool OpenFolder(string path)
    {
        OpenedFolders.Add(path);
        return FoldersWork;
    }

    public List<WindowsPage> OpenedPages { get; } = [];

    public bool OpenWindowsPage(WindowsPage page)
    {
        OpenedPages.Add(page);
        return true;
    }

    public List<string> StoreSearches { get; } = [];

    public bool OpenStoreSearch(string appName)
    {
        StoreSearches.Add(appName);
        return true;
    }
}

internal sealed class FakeDialogs : IDialogService
{
    public List<(string Title, string Message)> Messages { get; } = [];
    public int AboutShown { get; private set; }

    public void ShowMessage(string title, string message) => Messages.Add((title, message));

    public void ShowAbout() => AboutShown++;

    public bool ConfirmAnswer { get; set; }

    public List<PlanPreview> Previews { get; } = [];

    public bool ConfirmPlan(PlanPreview preview)
    {
        Previews.Add(preview);
        return ConfirmAnswer;
    }

    public bool ContinueAnswer { get; set; }

    public List<RestorePointResult> RestorePointQuestions { get; } = [];

    public bool AskContinueWithoutRestorePoint(RestorePointResult result)
    {
        RestorePointQuestions.Add(result);
        return ContinueAnswer;
    }

    public List<AntivirusCheckResult> AntivirusWarnings { get; } = [];

    public void ShowAntivirusWarning(AntivirusCheckResult result) => AntivirusWarnings.Add(result);

    public bool VendorToolAnswer { get; set; } = true;

    public List<VendorToolInfo> VendorToolQuestions { get; } = [];

    public bool ConfirmVendorTool(VendorToolInfo tool)
    {
        VendorToolQuestions.Add(tool);
        return VendorToolAnswer;
    }

    public List<(VendorToolInfo Tool, string Reason)> VendorToolProblems { get; } = [];

    public void ShowVendorToolProblem(VendorToolInfo tool, string reason) => VendorToolProblems.Add((tool, reason));
}
