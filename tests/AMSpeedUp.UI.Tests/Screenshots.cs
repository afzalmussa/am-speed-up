using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace AMSpeedUp.UI.Tests;

/// <summary>
/// Renders a window's contents to a PNG without showing it. CI sets SCREENSHOT_DIR and uploads the
/// pictures, so the layout can be checked without running the app. Without SCREENSHOT_DIR the
/// window is still rendered (which catches layout errors) but nothing is saved.
/// </summary>
internal static class Screenshots
{
    public static BitmapSource Render(Window window, string name, double width, double? height = null, double scale = 1.0, Action<FrameworkElement>? inspect = null)
    {
        var content = (UIElement)window.Content;
        window.Content = null;

        // Detached from the window, so copy what the window would have passed down.
        var frame = new Border
        {
            Background = window.Background,
            Child = content,
            Width = width,
            DataContext = window.DataContext,
            UseLayoutRounding = true,
        };
        TextElement.SetFontFamily(frame, window.FontFamily);
        TextElement.SetFontSize(frame, window.FontSize);
        TextElement.SetForeground(frame, window.Foreground);
        if (height is { } fixedHeight)
        {
            frame.Height = fixedHeight;
        }

        frame.Measure(new Size(width, height ?? double.PositiveInfinity));
        var finalHeight = height ?? frame.DesiredSize.Height;
        frame.Arrange(new Rect(0, 0, width, finalHeight));
        frame.UpdateLayout();
        inspect?.Invoke(frame);

        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(width * scale), (int)Math.Ceiling(finalHeight * scale),
            96 * scale, 96 * scale, PixelFormats.Pbgra32);
        bitmap.Render(frame);
        bitmap.Freeze();

        var folder = Environment.GetEnvironmentVariable("SCREENSHOT_DIR");
        if (!string.IsNullOrWhiteSpace(folder))
        {
            Directory.CreateDirectory(folder);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(folder, name + ".png"));
            encoder.Save(file);
        }

        frame.Child = null;
        window.Content = content;
        return bitmap;
    }

    /// <summary>True if the picture isn't a single flat colour, so something was actually drawn.</summary>
    public static bool HasContent(BitmapSource bitmap)
    {
        var stride = bitmap.PixelWidth * 4;
        var pixels = new byte[stride * bitmap.PixelHeight];
        bitmap.CopyPixels(pixels, stride, 0);
        var first = BitConverter.ToInt32(pixels, 0);
        for (var i = 4; i < pixels.Length; i += 4)
        {
            if (BitConverter.ToInt32(pixels, i) != first)
            {
                return true;
            }
        }

        return false;
    }
}
