using System.Windows;
using AMSpeedUp.Core.Actions;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Views;

public partial class PreviewWindow : Window
{
    public PreviewWindow(PlanPreview preview)
    {
        InitializeComponent();
        SourceInitialized += (_, _) => WindowSizing.FitToWorkArea(this);

        Preview = preview;
        TitleBlock.Text = preview.Plan.Title;
        IntroBlock.Text = SafetyText.PreviewIntro;
        NoticesList.ItemsSource = preview.Notices;
        SectionsList.ItemsSource = preview.Sections;
        ConfirmButton.Content = ConfirmText(preview);
        ConfirmButton.IsEnabled = !preview.Plan.IsEmpty;

        if (preview.Plan.Blocked.Count > 0)
        {
            BlockedHeading.Text = SafetyText.PreviewBlockedHeading;
            BlockedList.ItemsSource = preview.Plan.Blocked;
            BlockedPanel.Visibility = Visibility.Visible;
        }
    }

    public PlanPreview Preview { get; }

    public bool Confirmed { get; private set; }

    /// <summary>Says what will happen, for example "Yes, make 3 changes", or the test mode equivalent.</summary>
    public static string ConfirmText(PlanPreview preview)
    {
        var count = preview.Plan.Actions.Count;
        var changes = count == 1 ? "1 change" : $"{count} changes";
        return preview.IsDryRun ? $"Test {changes}" : $"Yes, make {changes}";
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        Confirmed = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
