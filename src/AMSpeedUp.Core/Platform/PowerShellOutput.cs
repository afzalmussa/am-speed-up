using System.Net;
using System.Text.RegularExpressions;

namespace AMSpeedUp.Core.Platform;

public static partial class PowerShellOutput
{
    /// <summary>
    /// Finds the JSON result a script printed last. Scripts end with ConvertTo-Json -Compress,
    /// so the last line starting with { or [ is the result, whatever else was printed before it.
    /// </summary>
    public static string? LastJsonLine(string output) =>
        output.ReplaceLineEndings("\n")
            .Split('\n')
            .Select(l => l.Trim())
            .LastOrDefault(l => l.StartsWith('{') || l.StartsWith('['));

    /// <summary>
    /// Windows PowerShell writes errors as CLIXML when its output is captured. This turns that
    /// back into the readable error text for the log. Plain text is returned unchanged.
    /// </summary>
    public static string CleanErrors(string errors)
    {
        if (!errors.TrimStart().StartsWith("#< CLIXML", StringComparison.Ordinal))
        {
            return errors.Trim();
        }

        var lines = ErrorString().Matches(errors)
            .Select(m => WebUtility.HtmlDecode(m.Groups[1].Value))
            .Select(DecodeEscapes);

        return string.Concat(lines).Trim();
    }

    // CLIXML escapes control characters as _xHHHH_, for example _x000D__x000A_ for a line break.
    private static string DecodeEscapes(string text) =>
        EscapedChar().Replace(text, m => ((char)Convert.ToInt32(m.Groups[1].Value, 16)).ToString());

    [GeneratedRegex("<S S=\"Error\">(.*?)</S>", RegexOptions.Singleline)]
    private static partial Regex ErrorString();

    [GeneratedRegex("_x([0-9A-Fa-f]{4})_")]
    private static partial Regex EscapedChar();
}
