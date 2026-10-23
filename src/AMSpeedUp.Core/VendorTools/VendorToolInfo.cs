using AMSpeedUp.Core.Presets;

namespace AMSpeedUp.Core.VendorTools;

/// <summary>Everything the app needs to know to use an antivirus maker's own removal tool (safety rule 5).</summary>
public sealed record VendorToolInfo
{
    public required VendorRemovalTool Tool { get; init; }

    public required string VendorName { get; init; }

    public required string ToolName { get; init; }

    /// <summary>Where the tool is downloaded from. Must be https and on one of <see cref="AllowedHosts"/>.</summary>
    public required Uri DownloadUrl { get; init; }

    public required string FileName { get; init; }

    /// <summary>The vendor's own domains. Downloads, including every redirect, must stay on these.</summary>
    public required IReadOnlyList<string> AllowedHosts { get; init; }

    /// <summary>Organisation names the tool must be digitally signed by.</summary>
    public required IReadOnlyList<string> ExpectedPublishers { get; init; }

    /// <summary>The vendor's own help page, opened if the tool can't be downloaded or checked.</summary>
    public required Uri HelpPage { get; init; }

    /// <summary>What the user clicks in the tool, in order.</summary>
    public required IReadOnlyList<string> Steps { get; init; }
}

/// <summary>
/// The official McAfee and Norton removal tools.
///
/// The download addresses below are the ones McAfee's and Norton's own community forums point to.
/// They couldn't be checked against the vendors' current pages while this was written, so check
/// them on a test PC. If an address is wrong or moves, nothing unsafe happens: the download or the
/// signature check fails, nothing is run, and the vendor's help page opens instead.
/// </summary>
public static class VendorToolCatalog
{
    public static VendorToolInfo McAfee { get; } = new()
    {
        Tool = VendorRemovalTool.McAfee,
        VendorName = "McAfee",
        ToolName = "McAfee Consumer Product Removal tool",
        DownloadUrl = new Uri("https://download.mcafee.com/products/licensed/cust_support_patches/MCPR.exe"),
        FileName = "MCPR.exe",
        AllowedHosts = ["mcafee.com"],
        ExpectedPublishers = ["McAfee, LLC", "McAfee, Inc."],
        HelpPage = new Uri("https://www.mcafee.com/support/s/article/000002577?language=en_US"),
        Steps =
        [
            "Click Next.",
            "Click Agree.",
            "Type the code shown on the screen exactly as it appears (capital letters matter), then click Next.",
            "Wait while it removes McAfee. This can take a few minutes.",
            "When it says it has finished, click Restart.",
        ],
    };

    public static VendorToolInfo Norton { get; } = new()
    {
        Tool = VendorRemovalTool.Norton,
        VendorName = "Norton",
        ToolName = "Norton Remove and Reinstall tool",
        DownloadUrl = new Uri("https://norton.com/nrnr"),
        FileName = "NRnR.exe",
        AllowedHosts = ["norton.com", "nortonlifelock.com", "gendigital.com"],
        ExpectedPublishers = ["Gen Digital Inc.", "NortonLifeLock Inc.", "Symantec Corporation"],
        HelpPage = new Uri("https://support.norton.com/sp/en/uk/home/current/solutions/v60392881"),
        Steps =
        [
            "Click Agree.",
            "Click Advanced Options. (If you only see a Remove button, click that instead.)",
            "Click Remove Only, then click Remove.",
            "Wait while it removes Norton. This can take a few minutes.",
            "When it asks, click Restart Now.",
        ],
    };

    public static VendorToolInfo For(VendorRemovalTool tool) => tool switch
    {
        VendorRemovalTool.McAfee => McAfee,
        VendorRemovalTool.Norton => Norton,
        _ => throw new ArgumentOutOfRangeException(nameof(tool), tool, "There's no removal tool for this."),
    };
}
