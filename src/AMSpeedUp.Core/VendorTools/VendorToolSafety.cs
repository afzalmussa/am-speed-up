using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace AMSpeedUp.Core.VendorTools;

public sealed record SignatureVerdict(bool IsTrusted, string Reason);

/// <summary>The checks a downloaded removal tool must pass before it's allowed to run.</summary>
public static class VendorToolSafety
{
    /// <summary>
    /// Only https, only the default port, and only the vendor's own domain or its subdomains.
    /// "mcafee.com.evil.example" and "notmcafee.com" don't count as mcafee.com.
    /// </summary>
    public static bool IsAllowedDownload(Uri uri, VendorToolInfo info)
    {
        if (!uri.IsAbsoluteUri || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        var host = uri.IdnHost.TrimEnd('.').ToLowerInvariant();
        return info.AllowedHosts.Any(allowed =>
            host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal));
    }

    /// <param name="status">Windows' verdict on the signature, as Get-AuthenticodeSignature reports it ("Valid" when good).</param>
    /// <param name="subject">The signing certificate's subject, for example: CN="McAfee, LLC", O="McAfee, LLC", L=San Jose, C=US.</param>
    public static SignatureVerdict CheckSignature(string? status, string? subject, VendorToolInfo info)
    {
        if (!string.Equals(status, "Valid", StringComparison.OrdinalIgnoreCase))
        {
            return new SignatureVerdict(false, $"The file's digital signature isn't valid (Windows says: {status ?? "no signature"}).");
        }

        if (string.IsNullOrWhiteSpace(subject))
        {
            return new SignatureVerdict(false, "The file has no publisher name on its signature.");
        }

        IReadOnlyList<string> names;
        try
        {
            names = PublisherNames(subject);
        }
        catch (CryptographicException)
        {
            return new SignatureVerdict(false, $"Couldn't read the publisher name: {subject}");
        }

        var match = names.FirstOrDefault(n => info.ExpectedPublishers.Contains(n, StringComparer.OrdinalIgnoreCase));
        return match is not null
            ? new SignatureVerdict(true, $"Signed by {match}.")
            : new SignatureVerdict(false, $"The file is signed by \"{string.Join(", ", names)}\", not {info.VendorName}.");
    }

    /// <summary>The organisation (O) and common name (CN) from a certificate subject.</summary>
    public static IReadOnlyList<string> PublisherNames(string subject)
    {
        var name = new X500DistinguishedName(subject);
        var names = new List<string>();
        foreach (var part in name.EnumerateRelativeDistinguishedNames())
        {
            var oid = part.GetSingleElementType().Value;
            // 2.5.4.10 is Organisation (O), 2.5.4.3 is Common Name (CN).
            if (oid is "2.5.4.10" or "2.5.4.3" && part.GetSingleElementValue() is { } value)
            {
                names.Add(value.Trim());
            }
        }

        return names;
    }
}
