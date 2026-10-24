using System.Text.RegularExpressions;
using AMSpeedUp.Core.Presets;

namespace AMSpeedUp.Core.Inventory;

public enum UninstallKind
{
    /// <summary>Removed with the maker's own removal tool (McAfee, Norton).</summary>
    VendorTool,

    /// <summary>Runs quietly in the background, with no windows to click through.</summary>
    Silent,

    /// <summary>The program's own uninstaller opens and the user follows its steps.</summary>
    Interactive,

    /// <summary>Windows doesn't say how to remove it.</summary>
    None,
}

public sealed record UninstallMethod(UninstallKind Kind, string? FileName = null, string? Arguments = null);

public static partial class UninstallMethods
{
    /// <summary>
    /// Picks the gentlest way to remove a desktop program: silently if possible, otherwise its own
    /// uninstaller. Silent options, in order: Windows Installer (msiexec), the program's published
    /// quiet command, then Inno Setup's standard silent switches.
    /// </summary>
    public static UninstallMethod Choose(InstalledApp app, VendorRemovalTool vendorTool = VendorRemovalTool.None)
    {
        if (vendorTool != VendorRemovalTool.None)
        {
            return new UninstallMethod(UninstallKind.VendorTool);
        }

        if (app.MsiProductCode is { } code)
        {
            return Msi(code);
        }

        if (CommandLineSplitter.Split(app.QuietUninstallString) is { } quiet)
        {
            return new UninstallMethod(UninstallKind.Silent, quiet.FileName, quiet.Arguments);
        }

        if (CommandLineSplitter.Split(app.UninstallString) is not { } normal)
        {
            return new UninstallMethod(UninstallKind.None);
        }

        // "MsiExec.exe /I{code}" opens the repair screen, so use the product code to remove instead.
        if (WindowsFileName(normal.FileName) is var program
            && (program.Equals("msiexec", StringComparison.OrdinalIgnoreCase) || program.Equals("msiexec.exe", StringComparison.OrdinalIgnoreCase))
            && MsiCodeInArguments().Match(normal.Arguments) is { Success: true } match)
        {
            return Msi(match.Value.ToUpperInvariant());
        }

        // Inno Setup uninstallers are always called unins000.exe (or 001...) and accept these switches.
        if (InnoUninstaller().IsMatch(WindowsFileName(normal.FileName)))
        {
            var arguments = (normal.Arguments + " /VERYSILENT /SUPPRESSMSGBOXES /NORESTART").Trim();
            return new UninstallMethod(UninstallKind.Silent, normal.FileName, arguments);
        }

        return new UninstallMethod(UninstallKind.Interactive, normal.FileName, normal.Arguments);
    }

    /// <summary>
    /// The file name from a Windows path. Path.GetFileName only splits on "\\" when running on
    /// Windows, and this code must behave the same everywhere so its tests mean something.
    /// </summary>
    public static string WindowsFileName(string path)
    {
        var slash = path.LastIndexOfAny(['\\', '/']);
        return slash >= 0 ? path[(slash + 1)..] : path;
    }

    private static UninstallMethod Msi(string productCode) =>
        new(UninstallKind.Silent, "msiexec.exe", $"/x {productCode} /qn /norestart");

    [GeneratedRegex(@"\{[0-9A-Fa-f]{8}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{4}-[0-9A-Fa-f]{12}\}")]
    private static partial Regex MsiCodeInArguments();

    [GeneratedRegex(@"^unins\d{3}\.exe$", RegexOptions.IgnoreCase)]
    private static partial Regex InnoUninstaller();
}
