using System.Net;
using AMSpeedUp.Core.Configuration;
using AMSpeedUp.Core.Logging;
using AMSpeedUp.Core.VendorTools;

namespace AMSpeedUp.Windows.VendorTools;

/// <summary>
/// Downloads a vendor's removal tool. Redirects are followed by hand so every hop can be checked:
/// the download must stay on https and on the vendor's own domains the whole way.
/// </summary>
public sealed class VendorToolDownloader(IAppLog log)
{
    private const int MaxRedirects = 5;
    private const long MaxBytes = 300L * 1024 * 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    /// <summary>Returns the downloaded file's path, or null with the reason logged.</summary>
    public async Task<(string? Path, string? Problem)> DownloadAsync(VendorToolInfo tool, CancellationToken cancellationToken)
    {
        using var handler = new SocketsHttpHandler { AllowAutoRedirect = false, AutomaticDecompression = DecompressionMethods.All };
        using var http = new HttpClient(handler) { Timeout = Timeout };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"AMSpeedUp/{AppInfo.Version}");

        var url = tool.DownloadUrl;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (!VendorToolSafety.IsAllowedDownload(url, tool))
            {
                return Fail($"The download tried to go to {url.Host}, which isn't one of {tool.VendorName}'s own websites.");
            }

            log.Info($"Downloading {tool.ToolName} from {url}.");
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                if (response.Headers.Location is not { } location)
                {
                    return Fail("The download redirected without saying where to.");
                }

                url = location.IsAbsoluteUri ? location : new Uri(url, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                return Fail($"{tool.VendorName}'s website answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            if (response.Content.Headers.ContentLength is > MaxBytes)
            {
                return Fail("The download is far larger than expected.");
            }

            // The user's own temporary folder, which other accounts on the PC can't write to.
            var folder = Path.Combine(Path.GetTempPath(), "AMSpeedUp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, tool.FileName);

            await using (var source = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false))
            await using (var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
                {
                    total += read;
                    if (total > MaxBytes)
                    {
                        file.Close();
                        TryDelete(path);
                        return Fail("The download is far larger than expected.");
                    }

                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                }
            }

            log.Info($"Downloaded {tool.ToolName} ({new FileInfo(path).Length:N0} bytes) to {path}.");
            return (path, null);
        }

        return Fail("The download redirected too many times.");

        (string?, string?) Fail(string problem)
        {
            log.Warning($"Couldn't download {tool.ToolName}: {problem}");
            return (null, problem);
        }
    }

    public void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
            var folder = Path.GetDirectoryName(path);
            if (folder is not null && Directory.Exists(folder) && !Directory.EnumerateFileSystemEntries(folder).Any())
            {
                Directory.Delete(folder);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Couldn't delete the downloaded file {path}.", ex);
        }
    }

    private static bool IsRedirect(HttpStatusCode status) =>
        status is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
            or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect;
}
