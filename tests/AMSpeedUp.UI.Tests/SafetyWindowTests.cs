using System.Windows;
using System.Windows.Controls;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Safety;
using AMSpeedUp.Services;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

public class SafetyWindowTests
{
    [Fact]
    public void Preview_lists_every_change_grouped_with_honest_undo_notes()
    {
        WpfHost.Run(() =>
        {
            var preview = SampleData.MixedPreview();
            var window = new PreviewWindow(preview);
            Screenshots.Render(window, "preview", 680);

            var texts = AllText(window);
            foreach (var action in preview.Plan.Actions)
            {
                Assert.Contains(action.Summary, texts);
            }

            foreach (var undo in new[] { UndoSupport.InApp, UndoSupport.ReinstallFromApp, UndoSupport.RestorePointOnly, UndoSupport.CannotUndo })
            {
                Assert.Contains(SafetyText.UndoNote(undo), texts);
            }

            Assert.Contains(texts, t => t.StartsWith("Uses McAfee's own removal tool.", StringComparison.Ordinal));
            Assert.Contains(SafetyText.PreviewRestorePointNotice, texts);
            Assert.Contains(SafetyText.PreviewBlockedHeading, texts);
            window.Close();
        });
    }

    [Fact]
    public void Preview_confirm_button_says_how_many_changes()
    {
        var preview = SampleData.MixedPreview();

        Assert.Equal("Yes, make 7 changes", PreviewWindow.ConfirmText(preview));
        Assert.Equal("Test 7 changes", PreviewWindow.ConfirmText(SampleData.MixedPreview(dryRun: true)));
    }

    [Fact]
    public void Preview_shows_test_mode_notice_in_dry_run()
    {
        WpfHost.Run(() =>
        {
            var window = new PreviewWindow(SampleData.MixedPreview(dryRun: true));
            Screenshots.Render(window, "preview-test-mode", 680);

            Assert.Contains(SafetyText.PreviewDryRunNotice, AllText(window));
            window.Close();
        });
    }

    [Fact]
    public void Closing_the_preview_without_clicking_confirm_is_not_a_yes()
    {
        WpfHost.Run(() =>
        {
            var window = new PreviewWindow(SampleData.MixedPreview());
            window.Close();

            Assert.False(window.Confirmed);
        });
    }

    [Fact]
    public void Enter_never_confirms_the_preview()
    {
        WpfHost.Run(() =>
        {
            var window = new PreviewWindow(SampleData.MixedPreview());
            Screenshots.Render(window, "preview-check", 680);

            Assert.DoesNotContain(FindAll<Button>(window), b => b.IsDefault);
            Assert.Contains(FindAll<Button>(window), b => b.IsCancel && (string)b.Content == "Cancel");
            window.Close();
        });
    }

    [Theory]
    [InlineData(RestorePointStatus.TurnedOff, "Cancel")]
    [InlineData(RestorePointStatus.Failed, "Cancel")]
    [InlineData(RestorePointStatus.TooSoon, "Continue")]
    public void Restore_point_question_makes_the_safe_choice_the_main_button(RestorePointStatus status, string primary)
    {
        var choice = DialogService.RestorePointChoice(new RestorePointResult(status, DateTimeOffset.Now.AddHours(-2)), new RecordingShell());

        Assert.Equal(primary, choice.PrimaryText);
    }

    [Theory]
    [InlineData(RestorePointStatus.TurnedOff, ChoiceResult.Primary, false)]
    [InlineData(RestorePointStatus.TurnedOff, ChoiceResult.Secondary, true)]
    [InlineData(RestorePointStatus.TurnedOff, ChoiceResult.Closed, false)]
    [InlineData(RestorePointStatus.Failed, ChoiceResult.Secondary, true)]
    [InlineData(RestorePointStatus.Failed, ChoiceResult.Closed, false)]
    [InlineData(RestorePointStatus.TooSoon, ChoiceResult.Primary, true)]
    [InlineData(RestorePointStatus.TooSoon, ChoiceResult.Secondary, false)]
    [InlineData(RestorePointStatus.TooSoon, ChoiceResult.Closed, false)]
    public void Closing_the_restore_point_question_always_means_cancel(RestorePointStatus status, ChoiceResult clicked, bool carriesOn)
    {
        Assert.Equal(carriesOn, DialogService.MeansContinue(status, clicked));
    }

    [Fact]
    public void Turned_off_offers_to_open_System_Protection()
    {
        var shell = new RecordingShell();
        var choice = DialogService.RestorePointChoice(new RestorePointResult(RestorePointStatus.TurnedOff), shell);

        Assert.Equal(SafetyText.TurnOnSystemRestore, choice.ExtraText);
        choice.ExtraAction!();
        Assert.Equal([WindowsPage.SystemProtection], shell.Pages);
    }

    [Fact]
    public void Antivirus_warning_offers_to_open_Windows_Security()
    {
        var shell = new RecordingShell();
        var choice = DialogService.AntivirusChoice(new AntivirusCheckResult(AntivirusVerdict.Unprotected), shell);

        Assert.True(choice.IsWarning);
        Assert.Equal(SafetyText.OpenWindowsSecurity, choice.ExtraText);
        choice.ExtraAction!();
        Assert.Equal([WindowsPage.VirusProtection], shell.Pages);
    }

    [Theory]
    [InlineData(RestorePointStatus.TurnedOff)]
    [InlineData(RestorePointStatus.TooSoon)]
    [InlineData(RestorePointStatus.Failed)]
    public void Restore_point_windows_render(RestorePointStatus status)
    {
        WpfHost.Run(() =>
        {
            var choice = DialogService.RestorePointChoice(new RestorePointResult(status, DateTimeOffset.Now.AddHours(-2)), new RecordingShell());
            var window = new ChoiceWindow(choice);
            var bitmap = Screenshots.Render(window, $"restore-point-{status.ToString().ToLowerInvariant()}", 580);

            Assert.True(Screenshots.HasContent(bitmap));
            Assert.Contains(choice.Heading, AllText(window));
            window.Close();
        });
    }

    [Theory]
    [InlineData(AntivirusVerdict.Unprotected)]
    [InlineData(AntivirusVerdict.Unknown)]
    public void Antivirus_warning_windows_render(AntivirusVerdict verdict)
    {
        WpfHost.Run(() =>
        {
            var window = new ChoiceWindow(DialogService.AntivirusChoice(new AntivirusCheckResult(verdict), new RecordingShell()));
            var bitmap = Screenshots.Render(window, $"antivirus-{verdict.ToString().ToLowerInvariant()}", 580);

            Assert.True(Screenshots.HasContent(bitmap));
            Assert.Contains(FindAll<Button>(window), b => (string)b.Content == SafetyText.OpenWindowsSecurity && b.Visibility == Visibility.Visible);
            window.Close();
        });
    }

    [Fact]
    public void A_choice_window_closed_without_a_click_reports_closed()
    {
        WpfHost.Run(() =>
        {
            var window = new ChoiceWindow(new Choice("Heading", "Body", "OK"));
            window.Close();

            Assert.Equal(ChoiceResult.Closed, window.Result);
        });
    }

    [Fact]
    public async Task Safety_prompts_reach_the_dialogs_on_the_UI_thread()
    {
        var dialogs = new FakeDialogs { ContinueAnswer = true };
        System.Windows.Threading.Dispatcher? ui = null;
        WpfHost.Run(() => ui = System.Windows.Threading.Dispatcher.CurrentDispatcher);
        var prompts = new UiSafetyPrompts(dialogs, ui!);

        var carryOn = await prompts.ContinueWithoutRestorePointAsync(new RestorePointResult(RestorePointStatus.TurnedOff), CancellationToken.None);
        await prompts.WarnAboutAntivirusAsync(new AntivirusCheckResult(AntivirusVerdict.Unprotected), CancellationToken.None);

        Assert.True(carryOn);
        Assert.Single(dialogs.RestorePointQuestions);
        Assert.Single(dialogs.AntivirusWarnings);
    }

    private static List<string> AllText(Window window)
    {
        // Text inside templates only exists in the visual tree after layout.
        var root = (DependencyObject)window.Content ?? window;
        return FindVisual<TextBlock>(root).Select(t => t.Text).Where(t => !string.IsNullOrEmpty(t)).ToList();
    }

    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject =>
        FindVisual<T>((DependencyObject)((Window)root).Content);

    private static IEnumerable<T> FindVisual<T>(DependencyObject root) where T : DependencyObject
    {
        if (root is T self)
        {
            yield return self;
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            foreach (var match in FindVisual<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i)))
            {
                yield return match;
            }
        }
    }

    private sealed class RecordingShell : IShellLauncher
    {
        public List<WindowsPage> Pages { get; } = [];

        public bool OpenUrl(string url) => true;

        public bool OpenFile(string path) => true;

        public bool OpenFolder(string path) => true;

        public bool OpenWindowsPage(WindowsPage page)
        {
            Pages.Add(page);
            return true;
        }

        public bool OpenStoreSearch(string appName) => true;

        public bool ShowInFolder(string filePath) => true;
    }
}
