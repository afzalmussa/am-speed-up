using System.Windows;
using System.Windows.Controls;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.ViewModels;
using AMSpeedUp.Views;

namespace AMSpeedUp.UI.Tests;

/// <summary>
/// Loads each window the way the app does. A missing style, broken binding source or bad
/// image path in the XAML throws here instead of crashing on a customer's PC.
/// </summary>
public class WindowSmokeTests
{
    private static MainViewModel CreateViewModel(bool dryRun = false) =>
        new(new FakeLog(), new FakeShell(), new FakeDialogs(), AppSettings.LoadEmbedded(),
            dryRun ? AppOptions.Parse(["--dry-run"]) : AppOptions.Default);

    private static void LayOut(FrameworkElement element)
    {
        element.Measure(new Size(1040, 680));
        element.Arrange(new Rect(0, 0, 1040, 680));
        element.UpdateLayout();
    }

    [Fact]
    public void Theme_resources_are_available()
    {
        WpfHost.Run(() =>
        {
            foreach (var key in new[] { "DarkBrush", "MintBrush", "LightBrush", "TextBrush", "HeadingFont", "BodyFont",
                                         "PrimaryButton", "SecondaryButton", "HeaderButton", "LinkButton", "Card",
                                         "AppWindowStyle", "MainTabControl", "MainTabItem", "BoolToVisibility" })
            {
                Assert.NotNull(Application.Current.TryFindResource(key));
            }
        });
    }

    [Fact]
    public void Main_window_loads_with_three_tabs_in_order()
    {
        WpfHost.Run(() =>
        {
            var window = new MainWindow { DataContext = CreateViewModel() };
            LayOut((FrameworkElement)window.Content);

            var tabs = FindAll<TabItem>(window).Select(t => t.Header?.ToString() ?? "").ToArray();
            Assert.Equal(["Remove bloat", "Startup", "Free up space"], tabs);
            window.Close();
        });
    }

    [Fact]
    public void Main_window_has_the_brand_logo()
    {
        WpfHost.Run(() =>
        {
            var window = new MainWindow { DataContext = CreateViewModel() };
            LayOut((FrameworkElement)window.Content);

            var logo = Assert.Single(FindAll<System.Windows.Controls.Image>(window));
            Assert.NotNull(logo.Source);
            window.Close();
        });
    }

    [Fact]
    public void Dry_run_banner_only_shows_in_dry_run()
    {
        WpfHost.Run(() =>
        {
            Assert.False(BannerVisible(dryRun: false));
            Assert.True(BannerVisible(dryRun: true));
        });

        static bool BannerVisible(bool dryRun)
        {
            var window = new MainWindow { DataContext = CreateViewModel(dryRun) };
            LayOut((FrameworkElement)window.Content);
            var banner = FindAll<TextBlock>(window).Single(t => t.Text.StartsWith("Test mode is on", StringComparison.Ordinal));
            var visible = IsVisibleInTree(banner);
            window.Close();
            return visible;
        }
    }

    [Fact]
    public void About_window_loads_with_website_and_donate_buttons()
    {
        WpfHost.Run(() =>
        {
            var window = new AboutWindow { DataContext = CreateViewModel() };
            LayOut((FrameworkElement)window.Content);

            Assert.Contains(FindAll<Button>(window), b => (b.Content as string) == "Donate");
            Assert.Contains(FindAll<Button>(window), b => (b.Content as string) == "Visit amsoftware.uk");
            window.Close();
        });
    }

    [Fact]
    public void Message_window_shows_heading_and_text()
    {
        WpfHost.Run(() =>
        {
            var window = new MessageWindow("A heading", "Some words");
            LayOut((FrameworkElement)window.Content);

            var texts = FindAll<TextBlock>(window).Select(t => t.Text).ToArray();
            Assert.Contains("A heading", texts);
            Assert.Contains("Some words", texts);
            window.Close();
        });
    }

    // Walks the logical tree, which exists before the window is shown on screen.
    private static IEnumerable<T> FindAll<T>(DependencyObject root) where T : DependencyObject
    {
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
        {
            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in FindAll<T>(child))
            {
                yield return nested;
            }
        }
    }

    // IsVisible is false for any window that isn't on screen, so check Visibility up the tree instead.
    // Stops below the Window itself, whose Visibility stays Collapsed until it is shown.
    private static bool IsVisibleInTree(FrameworkElement element)
    {
        for (DependencyObject? current = element; current is not null and not Window; current = LogicalTreeHelper.GetParent(current))
        {
            if (current is UIElement { Visibility: not Visibility.Visible })
            {
                return false;
            }
        }

        return true;
    }
}
