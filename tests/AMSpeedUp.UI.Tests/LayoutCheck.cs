using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace AMSpeedUp.UI.Tests;

/// <summary>
/// Looks for things that would be cut off on screen: single-line text that's wider than the space
/// it was given, and text or controls that run past the right-hand edge. Vertical overflow is fine,
/// because every page scrolls.
/// </summary>
internal static class LayoutCheck
{
    public static IReadOnlyList<string> FindClipped(FrameworkElement root)
    {
        var problems = new List<string>();
        Walk(root, root, problems);
        return problems;
    }

    private static void Walk(FrameworkElement root, DependencyObject node, List<string> problems)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
        {
            if (VisualTreeHelper.GetChild(node, i) is not FrameworkElement child || child.Visibility != Visibility.Visible)
            {
                continue;
            }

            // Scroll bars are meant to sit at the edge, and tooltips and pop-ups aren't on the page.
            if (child is ScrollBar or Popup)
            {
                continue;
            }

            if (child is TextBlock text && !string.IsNullOrWhiteSpace(text.Text) && text.ActualWidth > 0)
            {
                CheckText(text, problems);
                CheckRightEdge(root, text, problems);
            }
            else if (child is ButtonBase && child.ActualWidth > 0)
            {
                CheckRightEdge(root, child, problems);
            }

            Walk(root, child, problems);
        }
    }

    private static void CheckText(TextBlock text, List<string> problems)
    {
        if (text.TextWrapping != TextWrapping.NoWrap || text.TextTrimming != TextTrimming.None)
        {
            return;
        }

        var formatted = new FormattedText(
            text.Text,
            CultureInfo.CurrentUICulture,
            text.FlowDirection,
            new Typeface(text.FontFamily, text.FontStyle, text.FontWeight, text.FontStretch),
            text.FontSize,
            Brushes.Black,
            1.0);
        var needed = formatted.Width + text.Padding.Left + text.Padding.Right;
        if (needed > text.ActualWidth + 1)
        {
            problems.Add($"Text cut off ({needed:0} wide in {text.ActualWidth:0}): \"{text.Text}\"");
        }
    }

    private static void CheckRightEdge(FrameworkElement root, FrameworkElement element, List<string> problems)
    {
        var bounds = element.TransformToAncestor(root).TransformBounds(new Rect(element.RenderSize));
        if (bounds.Right > root.ActualWidth + 1 || bounds.Left < -1)
        {
            problems.Add($"Goes past the edge ({bounds.Left:0} to {bounds.Right:0} in {root.ActualWidth:0}): {Describe(element)}");
        }
    }

    private static string Describe(FrameworkElement element) => element switch
    {
        TextBlock text => $"\"{text.Text}\"",
        ContentControl { Content: string content } => $"{element.GetType().Name} \"{content}\"",
        _ => element.GetType().Name,
    };
}
