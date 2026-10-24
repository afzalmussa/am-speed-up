namespace AMSpeedUp.Core.Inventory;

public sealed record CommandLine(string FileName, string Arguments);

public static class CommandLineSplitter
{
    /// <summary>
    /// Splits an uninstall command from the registry into the program and its arguments.
    /// Handles quoted paths ("C:\Program Files\X\uninst.exe" /S) and the common unquoted form
    /// with spaces (C:\Program Files\X\uninst.exe /S). Returns null if no program can be found.
    /// </summary>
    public static CommandLine? Split(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var text = command.Trim();
        if (text[0] == '"')
        {
            var close = text.IndexOf('"', 1);
            if (close <= 1)
            {
                return null;
            }

            return new CommandLine(text[1..close], text[(close + 1)..].Trim());
        }

        // Unquoted: the program ends at the first ".exe" that's followed by a space or the end.
        var search = 0;
        while (true)
        {
            var exe = text.IndexOf(".exe", search, StringComparison.OrdinalIgnoreCase);
            if (exe < 0)
            {
                break;
            }

            var end = exe + 4;
            if (end == text.Length || char.IsWhiteSpace(text[end]))
            {
                return new CommandLine(text[..end], text[end..].Trim());
            }

            search = end;
        }

        // No ".exe": take the first word, for example "MsiExec /X{...}" or "rundll32 ...".
        var space = text.IndexOfAny([' ', '\t']);
        return space < 0 ? new CommandLine(text, "") : new CommandLine(text[..space], text[space..].Trim());
    }
}
