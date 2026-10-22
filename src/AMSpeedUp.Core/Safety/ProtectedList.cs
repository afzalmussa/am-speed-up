using AMSpeedUp.Core.Inventory;
using AMSpeedUp.Core.Matching;

namespace AMSpeedUp.Core.Safety;

public enum ProtectedField
{
    /// <summary>Store apps: the package name. Desktop programs: the uninstall key name.</summary>
    Id,
    DisplayName,
}

/// <param name="Examples">Real names this rule covers. Used to check that preset patterns can't reach them.</param>
public sealed record ProtectionRule(
    AppKind Kind,
    ProtectedField Field,
    string Pattern,
    string FriendlyName,
    string Reason,
    IReadOnlyList<string> Examples);

public sealed record ProtectionResult(bool IsProtected, string? Reason)
{
    public static ProtectionResult NotProtected { get; } = new(false, null);

    public static ProtectionResult Because(string reason) => new(true, reason);
}

/// <summary>
/// Things AM Speed Up must never remove (safety rule 3). The list is compiled into the app on
/// purpose: it can't be edited, switched off or overridden by a preset file. The action runner
/// checks it again immediately before every removal, however the removal was requested.
/// </summary>
public static class ProtectedList
{
    private const string WindowsNeedsIt = "Windows needs this to work properly.";
    private const string AppsNeedIt = "Other apps need this to run.";
    private const string HardwareNeedsIt = "Your PC's hardware needs this to work properly.";

    public static IReadOnlyList<ProtectionRule> Rules { get; } =
    [
        // Store apps, matched on package name
        Store("Microsoft.WindowsStore", "Microsoft Store", "You need it to install and update apps."),
        Store("Microsoft.StorePurchaseApp", "Microsoft Store purchases", "The Microsoft Store needs it."),
        Store("Microsoft.Services.Store.Engagement", "Microsoft Store services", "The Microsoft Store needs it."),
        Store("Microsoft.DesktopAppInstaller", "App Installer (winget)", "You need it to install and update apps."),
        Store("Microsoft.MicrosoftEdge*", "Microsoft Edge", "Windows and many apps use it to show web pages.",
              "Microsoft.MicrosoftEdge", "Microsoft.MicrosoftEdge.Stable", "Microsoft.MicrosoftEdgeDevToolsClient"),
        Store("Microsoft.Win32WebViewHost", "Web content host", "Windows and many apps use it to show web pages."),
        Store("Microsoft.WebView2*", "WebView2 Runtime", "Windows and many apps use it to show web pages.", "Microsoft.WebView2Runtime"),
        Store("Microsoft.NET.*", ".NET runtime", AppsNeedIt, "Microsoft.NET.Native.Framework.2.2", "Microsoft.NET.Native.Runtime.2.2"),
        Store("Microsoft.VCLibs.*", "Visual C++ runtime", AppsNeedIt, "Microsoft.VCLibs.140.00", "Microsoft.VCLibs.140.00.UWPDesktop"),
        Store("Microsoft.UI.Xaml.*", "Windows UI library", AppsNeedIt, "Microsoft.UI.Xaml.2.8"),
        Store("Microsoft.WindowsAppRuntime.*", "Windows App Runtime", AppsNeedIt, "Microsoft.WindowsAppRuntime.1.5"),
        Store("Microsoft.SecHealthUI", "Windows Security", "It protects your PC from viruses."),
        Store("Microsoft.GamingServices", "Gaming Services", "Games from the Microsoft Store and Xbox app need it."),
        Store("Microsoft.LanguageExperiencePack*", "Language pack", "Windows uses it to show your language.", "Microsoft.LanguageExperiencePacken-GB"),
        Store("Microsoft.*ImageExtension", "Picture format support", "Windows needs it to open some kinds of photos.",
              "Microsoft.HEIFImageExtension", "Microsoft.WebpImageExtension", "Microsoft.RawImageExtension"),
        Store("Microsoft.*VideoExtension*", "Video format support", "Windows needs it to play some kinds of video.",
              "Microsoft.HEVCVideoExtension", "Microsoft.VP9VideoExtensions", "Microsoft.AV1VideoExtension", "Microsoft.MPEG2VideoExtension"),
        Store("Microsoft.WebMediaExtensions", "Web media support", "Windows needs it to play some kinds of audio and video."),
        Store("Microsoft.Windows.*", "Windows system app", WindowsNeedsIt,
              "Microsoft.Windows.ShellExperienceHost", "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.Windows.CloudExperienceHost"),
        Store("MicrosoftWindows.*", "Windows system app", WindowsNeedsIt, "MicrosoftWindows.Client.CBS", "MicrosoftWindows.Client.WebExperience"),
        Store("Windows.*", "Windows system app", WindowsNeedsIt, "Windows.PrintDialog", "windows.immersivecontrolpanel"),

        // Store apps that come with hardware drivers
        Store("NVIDIACorp.NVIDIAControlPanel", "NVIDIA Control Panel", HardwareNeedsIt),
        Store("AdvancedMicroDevicesInc-2.*", "AMD graphics software", HardwareNeedsIt, "AdvancedMicroDevicesInc-2.AMDRadeonSoftware"),
        Store("AppUp.IntelGraphicsExperience", "Intel Graphics Command Center", HardwareNeedsIt),
        Store("RealtekSemiconductorCorp.*", "Realtek audio software", HardwareNeedsIt, "RealtekSemiconductorCorp.RealtekAudioControl"),
        Store("WavesAudio.*", "Waves audio software", HardwareNeedsIt, "WavesAudio.MaxxAudioProforDell2019"),
        Store("DolbyLaboratories.DolbyAudio*", "Dolby audio software", HardwareNeedsIt, "DolbyLaboratories.DolbyAudioPremium"),
        Store("SynapticsIncorporated.*", "Synaptics touchpad software", HardwareNeedsIt, "SynapticsIncorporated.SynapticsControlPanel"),
        Store("ELANMicroelectronicsCorpo.*", "ELAN touchpad software", HardwareNeedsIt, "ELANMicroelectronicsCorpo.ELANTrackPad"),

        // Desktop programs, matched on the name shown in "Installed apps"
        Desktop("Microsoft Edge*", "Microsoft Edge", "Windows and many apps use it to show web pages.",
                "Microsoft Edge", "Microsoft Edge Update", "Microsoft Edge WebView2 Runtime"),
        Desktop("Microsoft .NET*", ".NET runtime", AppsNeedIt, "Microsoft .NET Runtime - 8.0.11 (x64)"),
        Desktop("Microsoft Windows Desktop Runtime*", ".NET Desktop runtime", AppsNeedIt, "Microsoft Windows Desktop Runtime - 8.0.11 (x64)"),
        Desktop("Microsoft ASP.NET Core*", "ASP.NET Core runtime", AppsNeedIt, "Microsoft ASP.NET Core 8.0.11 - Shared Framework (x64)"),
        Desktop("Microsoft Visual C++*", "Visual C++ runtime", AppsNeedIt, "Microsoft Visual C++ 2015-2022 Redistributable (x64) - 14.40.33810"),
        Desktop("*DirectX*", "DirectX", "Games and graphics apps need it.", "Microsoft DirectX for Windows"),
        Desktop("Microsoft GameInput", "Microsoft GameInput", "Games need it to read controllers."),
        Desktop("Microsoft Update Health Tools", "Windows Update tools", "Windows Update needs it."),
        Desktop("Update for *", "Windows update", "It's part of a Windows update.", "Update for Windows 10 for x64-based Systems (KB5001716)"),
        Desktop("Windows Security*", "Windows Security", "It protects your PC from viruses.", "Windows Security"),
        Desktop("*Defender*", "Microsoft Defender", "It protects your PC from viruses.", "Microsoft Defender Antivirus"),
        Desktop("Windows Driver Package*", "Driver", HardwareNeedsIt, "Windows Driver Package - Intel (e1dexpress) Net"),
        Desktop("*Driver*", "Driver", HardwareNeedsIt, "Realtek High Definition Audio Driver", "NVIDIA Graphics Driver 560.94"),
        Desktop("*Chipset*", "Chipset software", HardwareNeedsIt, "AMD Chipset Software", "Intel(R) Chipset Device Software"),
        Desktop("*Bluetooth*", "Bluetooth software", HardwareNeedsIt, "Intel(R) Wireless Bluetooth(R)"),
        Desktop("Intel*", "Intel hardware software", HardwareNeedsIt,
                "Intel(R) Management Engine Components", "Intel(R) Rapid Storage Technology", "Intel® Serial IO"),
        Desktop("NVIDIA*", "NVIDIA hardware software", HardwareNeedsIt, "NVIDIA PhysX System Software 9.23.1019", "NVIDIA HD Audio Driver 1.3.40.14"),
        Desktop("AMD Software*", "AMD graphics software", HardwareNeedsIt, "AMD Software"),
        Desktop("Realtek*", "Realtek hardware software", HardwareNeedsIt, "Realtek Audio Console", "Realtek Card Reader"),
        Desktop("Synaptics*", "Synaptics touchpad software", HardwareNeedsIt, "Synaptics Pointing Device Driver"),
        Desktop("ELAN*", "ELAN touchpad software", HardwareNeedsIt, "ELAN Touchpad 15.8.13.1"),
        Desktop("Conexant*", "Conexant audio software", HardwareNeedsIt, "Conexant HD Audio"),
        Desktop("Waves*", "Waves audio software", HardwareNeedsIt, "Waves MaxxAudio Pro Application"),
    ];

    /// <summary>
    /// Decides whether an installed item must never be removed. Checks the hard-coded rules and
    /// the flags Windows itself uses to mark core components.
    /// </summary>
    public static ProtectionResult Check(InstalledApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        if (app.Kind == AppKind.StoreApp)
        {
            if (app.SignatureKind == PackageSignatureKind.System)
            {
                return ProtectionResult.Because("Part of Windows. " + WindowsNeedsIt);
            }

            if (app.IsFramework)
            {
                return ProtectionResult.Because("A shared component. " + AppsNeedIt);
            }

            if (app.IsNonRemovable)
            {
                return ProtectionResult.Because("Windows marks this as something that can't be removed.");
            }
        }
        else
        {
            if (app.IsSystemComponent)
            {
                return ProtectionResult.Because("Part of Windows or another program. " + WindowsNeedsIt);
            }

            if (IsWindowsPublisher(app.Publisher) || IsWindowsPublisher(app.SignedBy))
            {
                return ProtectionResult.Because("Part of Windows. " + WindowsNeedsIt);
            }
        }

        foreach (var rule in Rules)
        {
            if (rule.Kind != app.Kind)
            {
                continue;
            }

            var value = rule.Field == ProtectedField.Id ? app.Id : app.DisplayName;
            if (WildcardPattern.IsMatch(rule.Pattern, value)
                || (rule.Kind == AppKind.StoreApp && WildcardPattern.IsMatch(rule.Pattern, FamilyNameWithoutPublisherHash(app.PackageFamilyName))))
            {
                return ProtectionResult.Because($"{rule.FriendlyName}. {rule.Reason}");
            }
        }

        return ProtectionResult.NotProtected;
    }

    /// <summary>
    /// Checks whether a preset pattern could reach anything on the protected list. Used when
    /// loading presets, so a bad preset entry is rejected before anyone sees it.
    /// </summary>
    public static ProtectionResult CheckPattern(AppKind kind, string pattern)
    {
        foreach (var rule in Rules)
        {
            if (rule.Kind != kind)
            {
                continue;
            }

            // The preset pattern names something on the list, or is broad enough to reach a real protected name.
            var literal = pattern.Replace("*", string.Empty, StringComparison.Ordinal).Replace("?", string.Empty, StringComparison.Ordinal);
            if (WildcardPattern.IsMatch(rule.Pattern, literal)
                || WildcardPattern.IsMatch(pattern, rule.Pattern)
                || rule.Examples.Any(example => WildcardPattern.IsMatch(pattern, example)))
            {
                return ProtectionResult.Because($"{rule.FriendlyName}. {rule.Reason}");
            }
        }

        return ProtectionResult.NotProtected;
    }

    private static bool IsWindowsPublisher(string? name) =>
        name is not null
        && (name.Trim().Equals("Microsoft Windows", StringComparison.OrdinalIgnoreCase)
            || name.Trim().Equals("Microsoft Windows Publisher", StringComparison.OrdinalIgnoreCase));

    // "Microsoft.WindowsStore_8wekyb3d8bbwe" -> "Microsoft.WindowsStore"
    private static string? FamilyNameWithoutPublisherHash(string? familyName)
    {
        if (string.IsNullOrEmpty(familyName))
        {
            return null;
        }

        var underscore = familyName.LastIndexOf('_');
        return underscore > 0 ? familyName[..underscore] : familyName;
    }

    private static ProtectionRule Store(string pattern, string name, string reason, params string[] examples) =>
        new(AppKind.StoreApp, ProtectedField.Id, pattern, name, reason, examples.Length > 0 ? examples : [pattern]);

    private static ProtectionRule Desktop(string pattern, string name, string reason, params string[] examples) =>
        new(AppKind.DesktopProgram, ProtectedField.DisplayName, pattern, name, reason, examples.Length > 0 ? examples : [pattern]);
}
