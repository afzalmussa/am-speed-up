using System.Reflection;

namespace AMSpeedUp.Core.Configuration;

public static class AppInfo
{
    public const string ProductName = "AM Speed Up";

    /// <summary>The version from Directory.Build.props, or the release tag in CI, as "major.minor.patch".</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        var informational = typeof(AppInfo).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends "+<commit hash>". Users don't need to see that.
            var plus = informational.IndexOf('+');
            return plus >= 0 ? informational[..plus] : informational;
        }

        var version = typeof(AppInfo).Assembly.GetName().Version;
        return version is null ? "0.0.0" : $"{version.Major}.{version.Minor}.{version.Build}";
    }
}
