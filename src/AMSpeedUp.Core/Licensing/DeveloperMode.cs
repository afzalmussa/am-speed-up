namespace AMSpeedUp.Core.Licensing;

/// <summary>
/// Lets a developer test Pro features without buying a key: set the environment variable
/// AMSPEEDUP_DEV_PRO=1 and run a Debug build. Release builds, which is what people download,
/// don't contain this switch at all.
/// </summary>
public static class DeveloperMode
{
    public const string EnvironmentVariable = "AMSPEEDUP_DEV_PRO";

    public static bool IsCompiledIn { get; } =
#if DEBUG
        true;
#else
        false;
#endif

    public static bool IsOn(Func<string, string?>? readVariable = null)
    {
        if (!IsCompiledIn)
        {
            return false;
        }

        var value = (readVariable ?? Environment.GetEnvironmentVariable)(EnvironmentVariable);
        return value is "1" or "true" or "TRUE" or "True";
    }
}
