using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using AMSpeedUp.Core.Licensing;

namespace AMSpeedUp.Windows.Licensing;

/// <summary>
/// Saves the activation encrypted with Windows' Data Protection API, tied to this PC. A copy of the
/// file is useless on another PC. Machine scope is used so every administrator on this PC, and the
/// monthly tune-up, can read it.
/// </summary>
public sealed class DpapiLicenceStore(string path) : ILicenceStore
{
    // Extra input to the encryption, so other programs using DPAPI on this PC can't read it by accident.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("AMSoftware.SpeedUp.Licence.v1");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public StoredLicence? Load()
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var json = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.LocalMachine);
        return JsonSerializer.Deserialize<StoredLicence>(json, JsonOptions);
    }

    public void Save(StoredLicence licence)
    {
        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder))
        {
            Directory.CreateDirectory(folder);
        }

        var protectedBytes = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(licence, JsonOptions), Entropy, DataProtectionScope.LocalMachine);
        var temp = path + ".tmp";
        File.WriteAllBytes(temp, protectedBytes);
        File.Move(temp, path, overwrite: true);
    }

    public void Delete()
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
