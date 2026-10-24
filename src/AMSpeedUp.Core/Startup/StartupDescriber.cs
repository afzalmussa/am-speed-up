using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Core.Startup;

/// <param name="Advice">Null when the item isn't in the known list.</param>
public sealed record StartupDescription(string DisplayName, string Description, StartupAdvice? Advice, ProtectionResult Protection);

public static class StartupDescriber
{
    public static StartupDescription Describe(StartupItem item, IEnumerable<KnownStartupItem> known)
    {
        var match = KnownStartupItems.Find(known, item);
        return new StartupDescription(
            match?.Name ?? item.NameWithoutExtension,
            match?.Description ?? KnownStartupItems.UnknownDescription,
            match?.Advice,
            StartupProtection.CheckTurningOff(item));
    }
}
