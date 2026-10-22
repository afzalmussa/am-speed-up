using AMSpeedUp.Core.Inventory;

namespace AMSpeedUp.Core.Presets;

public enum Recommendation
{
    /// <summary>Safe to remove and most people won't miss it. Ticked by default.</summary>
    Remove,

    /// <summary>Some people use it. Shown but not ticked.</summary>
    Optional,

    /// <summary>Worth keeping, for example a tool that installs driver or BIOS updates. Shown, not ticked, with the reason.</summary>
    Keep,
}

public enum PresetTier
{
    Free,
    Pro,
}

/// <summary>The antivirus makers whose own removal tools are used instead of the normal uninstaller (safety rule 5).</summary>
public enum VendorRemovalTool
{
    None,
    McAfee,
    Norton,
}

public sealed record PresetEntry
{
    public required AppKind Kind { get; init; }

    /// <summary>
    /// Wildcard pattern. Store apps: matched against the package name.
    /// Desktop programs: matched against the name shown in "Installed apps".
    /// </summary>
    public required string Match { get; init; }

    /// <summary>Optional wildcard pattern for the publisher (desktop programs only).</summary>
    public string? Publisher { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required Recommendation Recommendation { get; init; }

    /// <summary>Removing this leaves the PC relying on Microsoft Defender, so Defender is checked afterwards.</summary>
    public bool IsAntivirus { get; init; }

    public VendorRemovalTool VendorTool { get; init; } = VendorRemovalTool.None;
}

public sealed record Preset
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required PresetTier Tier { get; init; }

    public required IReadOnlyList<PresetEntry> Entries { get; init; }

    /// <summary>Where the preset came from, for log messages. Usually the file name.</summary>
    public string Source { get; init; } = "";
}
