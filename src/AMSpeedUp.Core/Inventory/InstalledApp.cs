namespace AMSpeedUp.Core.Inventory;

public enum AppKind
{
    /// <summary>An app from the Microsoft Store (an Appx/MSIX package).</summary>
    StoreApp,

    /// <summary>A traditional program listed in "Installed apps" / "Programs and Features".</summary>
    DesktopProgram,
}

/// <summary>How Windows says a Store app package was signed.</summary>
public enum PackageSignatureKind
{
    Unknown,
    None,
    Developer,
    Enterprise,
    Store,

    /// <summary>Part of Windows itself. Never removable.</summary>
    System,
}

/// <summary>
/// Something installed on the PC that the app might offer to remove.
/// Store apps and desktop programs share one type so the safety checks work the same way for both.
/// </summary>
public sealed record InstalledApp
{
    public required AppKind Kind { get; init; }

    /// <summary>
    /// Store apps: the package name, for example "king.com.CandyCrushSaga".
    /// Desktop programs: the uninstall registry key name.
    /// </summary>
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public string? Publisher { get; init; }

    // Store app details

    public string? PackageFamilyName { get; init; }

    public string? PackageFullName { get; init; }

    public PackageSignatureKind SignatureKind { get; init; } = PackageSignatureKind.Unknown;

    /// <summary>A shared library other apps depend on, such as the Visual C++ runtime.</summary>
    public bool IsFramework { get; init; }

    /// <summary>Windows marks this package as one that can't be removed.</summary>
    public bool IsNonRemovable { get; init; }

    // Desktop program details

    /// <summary>Windows hides this entry from "Installed apps" because it's part of something bigger.</summary>
    public bool IsSystemComponent { get; init; }

    public string? UninstallString { get; init; }

    public string? QuietUninstallString { get; init; }

    /// <summary>The name on the digital signature of the program's files, when known.</summary>
    public string? SignedBy { get; init; }
}
