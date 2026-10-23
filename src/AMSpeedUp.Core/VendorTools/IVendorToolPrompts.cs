namespace AMSpeedUp.Core.VendorTools;

/// <summary>What the user is told before and around running a vendor's removal tool.</summary>
public interface IVendorToolPrompts
{
    /// <summary>Explains the steps the tool will ask for. Returns false if the user chooses to skip it.</summary>
    Task<bool> ReadyToRunAsync(VendorToolInfo tool, CancellationToken cancellationToken);

    /// <summary>The tool couldn't be downloaded or checked, so nothing was run. The vendor's help page is opened too.</summary>
    Task ShowCannotRunAsync(VendorToolInfo tool, string reason, CancellationToken cancellationToken);
}

/// <summary>
/// Removal tools often restart the PC. This makes sure the antivirus check still happens:
/// the app opens once after the restart, checks Microsoft Defender, and warns if needed.
/// </summary>
public interface IAntivirusCheckAfterRestart
{
    void Schedule();

    void Cancel();
}

public static class VendorToolText
{
    public static string ReadyHeading(VendorToolInfo tool) => $"Removing {tool.VendorName}";

    public static string ReadyBody(VendorToolInfo tool) =>
        $"{tool.VendorName}'s own removal tool will open next. In it:\n\n" +
        string.Join("\n", tool.Steps.Select((step, i) => $"{i + 1}. {step}")) +
        "\n\nRestarting is fine. AM Speed Up will open again afterwards to check that Microsoft Defender is protecting your PC.";

    public static string CannotRunHeading(VendorToolInfo tool) => $"{tool.VendorName} wasn't removed";

    public static string CannotRunBody(VendorToolInfo tool) =>
        $"AM Speed Up couldn't safely get {tool.VendorName}'s removal tool, so nothing was run.\n\n" +
        $"We've opened {tool.VendorName}'s own help page in your web browser. Follow the steps there to remove {tool.VendorName}.";
}
