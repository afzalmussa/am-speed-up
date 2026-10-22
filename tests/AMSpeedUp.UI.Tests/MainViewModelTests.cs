using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.ViewModels;

namespace AMSpeedUp.UI.Tests;

public class MainViewModelTests
{
    private readonly FakeLog _log = new();
    private readonly FakeShell _shell = new();
    private readonly FakeDialogs _dialogs = new();

    private MainViewModel Create(AppSettings? settings = null, AppOptions? options = null) =>
        new(_log, _shell, _dialogs, settings ?? new AppSettings { DonateUrl = "https://ko-fi.com/afzalm" }, options ?? AppOptions.Default);

    [Fact]
    public void Shows_free_version_until_licensing_is_built()
    {
        Assert.Equal("Free version", Create().LicenceStatusText);
    }

    [Fact]
    public void Version_text_includes_the_version_number()
    {
        Assert.Equal($"Version {AppInfo.Version}", Create().VersionText);
    }

    [Fact]
    public void Dry_run_flag_reaches_the_view_model()
    {
        Assert.True(Create(options: AppOptions.Parse(["--dry-run"])).IsDryRun);
        Assert.False(Create().IsDryRun);
    }

    [Fact]
    public void View_log_opens_todays_log_file_and_logs_that_it_did()
    {
        Create().ViewLogCommand.Execute(null);

        Assert.Equal([_log.CurrentLogFile], _shell.OpenedFiles);
        Assert.Empty(_shell.OpenedFolders);
        Assert.Contains(_log.Entries, e => e.Level == LogLevel.Info && e.Message.Contains("opened the log"));
    }

    [Fact]
    public void View_log_falls_back_to_the_log_folder()
    {
        _shell.FilesWork = false;

        Create().ViewLogCommand.Execute(null);

        Assert.Equal([_log.LogDirectory], _shell.OpenedFolders);
        Assert.Empty(_dialogs.Messages);
    }

    [Fact]
    public void View_log_tells_the_user_where_the_log_is_if_nothing_opens()
    {
        _shell.FilesWork = false;
        _shell.FoldersWork = false;

        Create().ViewLogCommand.Execute(null);

        var message = Assert.Single(_dialogs.Messages);
        Assert.Contains(_log.LogDirectory, message.Message);
    }

    [Fact]
    public void Footer_links_open_the_right_pages()
    {
        var vm = Create();

        vm.CheckForUpdatesCommand.Execute(null);
        vm.ReportProblemCommand.Execute(null);
        vm.GetHelpCommand.Execute(null);
        vm.OpenWebsiteCommand.Execute(null);
        vm.OpenSourceCodeCommand.Execute(null);

        Assert.Equal([AppLinks.Releases, AppLinks.Issues, AppLinks.Contact, AppLinks.Website, AppLinks.Repository], _shell.OpenedUrls);
    }

    [Fact]
    public void If_the_browser_will_not_open_the_address_is_shown_instead()
    {
        _shell.UrlsWork = false;

        Create().GetHelpCommand.Execute(null);

        var message = Assert.Single(_dialogs.Messages);
        Assert.Contains(AppLinks.Contact, message.Message);
    }

    [Fact]
    public void Donate_opens_the_configured_page()
    {
        var vm = Create();

        Assert.True(vm.CanDonate);
        vm.DonateCommand.Execute(null);

        Assert.Equal(["https://ko-fi.com/afzalm"], _shell.OpenedUrls);
    }

    [Fact]
    public void Donate_is_hidden_and_does_nothing_when_not_set_up()
    {
        var vm = Create(new AppSettings { DonateUrl = "" });

        Assert.False(vm.CanDonate);
        vm.DonateCommand.Execute(null);

        Assert.Empty(_shell.OpenedUrls);
    }

    [Fact]
    public void About_opens_the_about_window()
    {
        Create().ShowAboutCommand.Execute(null);

        Assert.Equal(1, _dialogs.AboutShown);
    }

    [Fact]
    public void Licence_key_button_explains_it_is_not_ready_yet()
    {
        Create().EnterLicenceKeyCommand.Execute(null);

        Assert.Single(_dialogs.Messages);
    }
}
