using System.Text.Json;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Windows.Safety;

/// <summary>Reads Microsoft Defender's status and the antivirus products registered with Windows Security Center.</summary>
public sealed class WindowsAntivirusStatusService(IPowerShellRunner powerShell, IAppLog log) : IAntivirusStatusService
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(1);

    private const string Script = """
        $result = [ordered]@{ defender = $null; defenderError = $null; products = @(); productsError = $null }

        try {
            $status = Get-MpComputerStatus -ErrorAction Stop
            $result.defender = [ordered]@{
                antivirusEnabled = [bool]$status.AntivirusEnabled
                realTimeProtectionEnabled = [bool]$status.RealTimeProtectionEnabled
                amRunningMode = [string]$status.AMRunningMode
            }
        } catch {
            $result.defenderError = $_.Exception.Message
        }

        try {
            $result.products = @(Get-CimInstance -Namespace 'root/SecurityCenter2' -ClassName 'AntiVirusProduct' -ErrorAction Stop |
                ForEach-Object { [ordered]@{ name = [string]$_.displayName; state = [int]$_.productState } })
        } catch {
            $result.productsError = $_.Exception.Message
        }

        $result | ConvertTo-Json -Compress -Depth 4
        """;

    public async Task<AntivirusSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var run = await powerShell.RunAsync(Script, "Checking Microsoft Defender and other antivirus", Timeout, cancellationToken).ConfigureAwait(false);

        var json = PowerShellOutput.LastJsonLine(run.Output);
        if (json is null)
        {
            var reason = run.TimedOut ? "Timed out." : $"No result. Exit code {run.ExitCode}. {run.Errors}";
            return new AntivirusSnapshot(null, reason, null, reason);
        }

        try
        {
            return WindowsResultParsers.ParseAntivirusSnapshot(json);
        }
        catch (JsonException ex)
        {
            log.Error("Couldn't read the antivirus status.", ex);
            return new AntivirusSnapshot(null, ex.Message, null, ex.Message);
        }
    }
}
