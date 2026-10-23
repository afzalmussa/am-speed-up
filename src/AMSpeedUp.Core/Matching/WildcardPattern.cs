using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace AMSpeedUp.Core.Matching;

/// <summary>
/// Simple, case-insensitive wildcard matching: <c>*</c> matches any run of characters,
/// <c>?</c> matches exactly one. Everything else is literal. The whole text must match.
/// </summary>
public static class WildcardPattern
{
    private static readonly ConcurrentDictionary<string, Regex> Cache = new(StringComparer.Ordinal);

    public static bool IsMatch(string pattern, string? text)
    {
        if (text is null)
        {
            return false;
        }

        var regex = Cache.GetOrAdd(pattern, static p => new Regex(
            "^" + Regex.Escape(p).Replace(@"\*", ".*", StringComparison.Ordinal).Replace(@"\?", ".", StringComparison.Ordinal) + "$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline,
            TimeSpan.FromSeconds(1)));

        return regex.IsMatch(text.Trim());
    }

    public static bool HasWildcards(string pattern) => pattern.AsSpan().IndexOfAny('*', '?') >= 0;

    /// <summary>Number of characters that aren't wildcards. Used to reject patterns that are too broad.</summary>
    public static int LiteralLength(string pattern) => pattern.Count(c => c is not ('*' or '?'));
}
