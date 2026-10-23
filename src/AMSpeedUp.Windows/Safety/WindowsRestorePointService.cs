using System.Text.Json;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.Safety;

namespace AMSpeedUp.Windows.Safety;

/// <summary>
/// Creates System Restore points with Checkpoint-Computer. Because Windows can report success
/// without making one, the newest restore point is read before and after to see what really happened.
/// </summary>
public sealed class WindowsRestorePointService(IPowerShellRunner powerShell, IAppLog log, TimeProvider? time = null) : IRestorePointService
{
    // Making a restore point can take several minutes on a slow disk.
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    private const string Script = """
        $result = [ordered]@{ commandSucceeded = $false; error = $null; warnings = @(); policyDisabled = $false; before = $null; after = $null }

        function Get-Latest {
            try {
                $point = Get-ComputerRestorePoint -ErrorAction Stop | Sort-Object SequenceNumber | Select-Object -Last 1
                if ($null -eq $point) { return $null }
                $created = [Management.ManagementDateTimeConverter]::ToDateTime($point.CreationTime).ToUniversalTime().ToString('o')
                return [ordered]@{ sequence = [long]$point.SequenceNumber; created = $created }
            } catch { return $null }
        }

        $policy = Get-ItemProperty 'HKLM:\SOFTWARE\Policies\Microsoft\Windows NT\SystemRestore' -ErrorAction SilentlyContinue
        if ($policy -and $policy.DisableSR -eq 1) { $result.policyDisabled = $true }

        $result.before = Get-Latest
        try {
            Checkpoint-Computer -Description $description -RestorePointType 'MODIFY_SETTINGS' -ErrorAction Stop -WarningAction SilentlyContinue -WarningVariable cpWarnings
            $result.commandSucceeded = $true
        } catch {
            $result.error = $_.Exception.Message
        }
        $result.warnings = @($cpWarnings | ForEach-Object { $_.Message })
        $result.after = Get-Latest

        $result | ConvertTo-Json -Compress -Depth 4
        """;

    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<RestorePointResult> CreateAsync(string description, CancellationToken cancellationToken = default)
    {
        var script = $"$description = {PowerShellQuoting.Literal(description)}\n{Script}";
        var run = await powerShell.RunAsync(script, "Creating a System Restore point", Timeout, cancellationToken).ConfigureAwait(false);

        if (run.TimedOut)
        {
            return new RestorePointResult(RestorePointStatus.Failed, TechnicalDetail: "Windows took too long to create the restore point.");
        }

        var json = PowerShellOutput.LastJsonLine(run.Output);
        if (json is null)
        {
            return new RestorePointResult(RestorePointStatus.Failed, TechnicalDetail: $"No result from PowerShell. Exit code {run.ExitCode}. {run.Errors}");
        }

        try
        {
            var attempt = WindowsResultParsers.ParseRestorePointAttempt(json);
            return RestorePointClassifier.Classify(attempt, _time.GetUtcNow());
        }
        catch (JsonException ex)
        {
            log.Error("Couldn't read the restore point result.", ex);
            return new RestorePointResult(RestorePointStatus.Failed, TechnicalDetail: "Couldn't read the result: " + ex.Message);
        }
    }
}
