using System.Text.Json;
using AMSpeedUp.Core.Platform;
using AMSpeedUp.Core.VendorTools;

namespace AMSpeedUp.Windows.VendorTools;

/// <summary>Asks Windows whether a file's digital signature is valid and who signed it.</summary>
public sealed class AuthenticodeChecker(IPowerShellRunner powerShell)
{
    private const string Script = """
        $signature = Get-AuthenticodeSignature -LiteralPath $path
        [ordered]@{
            status = [string]$signature.Status
            subject = if ($signature.SignerCertificate) { [string]$signature.SignerCertificate.Subject } else { $null }
        } | ConvertTo-Json -Compress
        """;

    public async Task<SignatureVerdict> CheckAsync(string path, VendorToolInfo tool, CancellationToken cancellationToken)
    {
        var run = await powerShell.RunAsync($"$path = {PowerShellQuoting.Literal(path)}\n{Script}", $"Checking the signature of {tool.FileName}", TimeSpan.FromMinutes(2), cancellationToken)
            .ConfigureAwait(false);

        var json = PowerShellOutput.LastJsonLine(run.Output);
        if (json is null)
        {
            return new SignatureVerdict(false, "Windows couldn't check the file's signature.");
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            var status = root.TryGetProperty("status", out var s) ? s.GetString() : null;
            var subject = root.TryGetProperty("subject", out var j) && j.ValueKind == JsonValueKind.String ? j.GetString() : null;
            return VendorToolSafety.CheckSignature(status, subject, tool);
        }
        catch (JsonException)
        {
            return new SignatureVerdict(false, "Windows couldn't check the file's signature.");
        }
    }
}
