using AMSpeedUp.Core.Actions;

namespace AMSpeedUp.Core.Cleanup;

/// <summary>Clear one kind of clutter. Deleted files can't be brought back, and the preview says so.</summary>
public sealed record CleanupAction(CleanupTarget Target) : PlannedAction
{
    public override string Summary
    {
        get
        {
            var size = ByteSize.Format(Target.Bytes);
            return Target.Category switch
            {
                CleanupCategory.RecycleBin => $"Empty the Recycle Bin ({size})",
                CleanupCategory.WindowsUpdate => $"Tidy up Windows Update leftovers (at least {size})",
                CleanupCategory.EdgeCache or CleanupCategory.ChromeCache or CleanupCategory.FirefoxCache => $"Clear the {Target.Info.BrowserName} cache ({size})",
                CleanupCategory.WindowsTemp => $"Delete Windows temporary files ({size})",
                _ => $"Delete your temporary files ({size})",
            };
        }
    }

    public override string? Detail => Target.Category switch
    {
        CleanupCategory.RecycleBin => "Everything in the Recycle Bin is deleted for good.",
        CleanupCategory.WindowsUpdate => "This part can take a long time, sometimes more than half an hour. You can keep using your PC.",
        CleanupCategory.EdgeCache or CleanupCategory.ChromeCache or CleanupCategory.FirefoxCache =>
            $"Your passwords, bookmarks and history aren't touched. Skipped if {Target.Info.BrowserName} is open.",
        _ => "Only files more than a day old, so nothing that's in use is touched.",
    };

    public override UndoSupport Undo => UndoSupport.CannotUndo;
}

/// <summary>A short live message about what's happening, for long jobs like the Windows Update clean-up.</summary>
public sealed class ActivityStatus
{
    public event Action<string>? Changed;

    public void Report(string message) => Changed?.Invoke(message);
}

public static class DismProgress
{
    /// <summary>
    /// Reads the last percentage from DISM's progress bar, for example "[====  40.0%    ]" gives 40.
    /// Returns null if there isn't one.
    /// </summary>
    public static double? LastPercent(string output)
    {
        double? last = null;
        var index = output.IndexOf('%');
        while (index > 0)
        {
            var start = index - 1;
            while (start >= 0 && (char.IsDigit(output[start]) || output[start] == '.'))
            {
                start--;
            }

            if (double.TryParse(output.AsSpan(start + 1, index - start - 1), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value)
                && value is >= 0 and <= 100)
            {
                last = value;
            }

            index = output.IndexOf('%', index + 1);
        }

        return last;
    }
}
