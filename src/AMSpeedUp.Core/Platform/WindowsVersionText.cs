namespace AMSpeedUp.Core.Platform;

public static class WindowsVersionText
{
    /// <summary>First build number of Windows 11.</summary>
    public const int FirstWindows11Build = 22000;

    /// <summary>
    /// Builds a description like "Windows 11 Pro 23H2 (build 22631)" from the registry values.
    /// Windows 11 still reports "Windows 10" in ProductName, so the build number decides.
    /// </summary>
    public static string Describe(string? productName, string? displayVersion, string? currentBuild)
    {
        var name = string.IsNullOrWhiteSpace(productName) ? "Windows" : productName.Trim();

        if (int.TryParse(currentBuild, out var build) && build >= FirstWindows11Build)
        {
            name = name.Replace("Windows 10", "Windows 11", StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(displayVersion))
        {
            name = $"{name} {displayVersion.Trim()}";
        }

        return string.IsNullOrWhiteSpace(currentBuild) ? name : $"{name} (build {currentBuild.Trim()})";
    }
}
