using System.Windows;

namespace AMSpeedUp.Views;

public enum ChoiceResult
{
    Primary,
    Secondary,

    /// <summary>Closed with the X or Escape. Callers treat this as the safe option.</summary>
    Closed,
}

/// <summary>What a <see cref="ChoiceWindow"/> shows. The primary button is the safest choice wherever there is one.</summary>
public sealed record Choice(
    string Heading,
    string Body,
    string PrimaryText,
    string? SecondaryText = null,
    string? ExtraText = null,
    Action? ExtraAction = null,
    bool IsWarning = false);

public partial class ChoiceWindow : Window
{
    private readonly Choice _choice;

    public ChoiceWindow(Choice choice)
    {
        InitializeComponent();
        _choice = choice;
        MaxHeight = Math.Min(MaxHeight, SystemParameters.WorkArea.Height - 16);

        HeadingBlock.Text = choice.Heading;
        BodyBlock.Text = choice.Body;
        PrimaryButton.Content = choice.PrimaryText;
        WarningStrip.Visibility = choice.IsWarning ? Visibility.Visible : Visibility.Collapsed;

        if (choice.SecondaryText is not null)
        {
            SecondaryButton.Content = choice.SecondaryText;
            SecondaryButton.Visibility = Visibility.Visible;
        }

        if (choice.ExtraText is not null && choice.ExtraAction is not null)
        {
            ExtraButton.Content = choice.ExtraText;
            ExtraButton.Visibility = Visibility.Visible;
        }
    }

    public ChoiceResult Result { get; private set; } = ChoiceResult.Closed;

    public static ChoiceResult Show(Window? owner, Choice choice)
    {
        var window = new ChoiceWindow(choice);
        if (owner is not null)
        {
            window.Owner = owner;
        }
        else
        {
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowInTaskbar = true;
        }

        window.ShowDialog();
        return window.Result;
    }

    private void Primary_Click(object sender, RoutedEventArgs e)
    {
        Result = ChoiceResult.Primary;
        Close();
    }

    private void Secondary_Click(object sender, RoutedEventArgs e)
    {
        Result = ChoiceResult.Secondary;
        Close();
    }

    // Opens something (like Windows Security) without closing, so the user can come back and decide.
    private void Extra_Click(object sender, RoutedEventArgs e) => _choice.ExtraAction?.Invoke();
}
