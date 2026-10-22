using System.Windows;

namespace AMSpeedUp.Views;

internal static class WindowSizing
{
    /// <summary>
    /// Shrinks a window that would be taller or wider than the screen, which happens on small
    /// laptops at 125% or 150% scaling. Content scrolls instead of being cut off.
    /// Call from SourceInitialized, before WPF applies WindowStartupLocation.
    /// </summary>
    public static void FitToWorkArea(Window window)
    {
        var area = SystemParameters.WorkArea;
        const double margin = 16;

        window.MinWidth = Math.Min(window.MinWidth, area.Width - margin);
        window.MinHeight = Math.Min(window.MinHeight, area.Height - margin);
        window.Width = Math.Min(window.Width, area.Width - margin);
        window.Height = Math.Min(window.Height, area.Height - margin);
    }
}
