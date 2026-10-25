using AMSpeedUp.Core.Licensing;
using AMSpeedUp.Windows.Licensing;

namespace AMSpeedUp.Windows.Tests;

public sealed class DpapiLicenceStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "amspeedup-licence-test-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_folder))
        {
            Directory.Delete(_folder, recursive: true);
        }
    }

    private string FilePath => Path.Combine(_folder, "licence.dat");

    private static StoredLicence Sample => new()
    {
        Key = "38B1460A-5104-4067-A91D-77B872934D51",
        InstanceId = "47596ad9-a811-4ebf-ac8a-03fc7b6d2a17",
        ActivatedAt = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero),
        LastConfirmedAt = new DateTimeOffset(2026, 10, 25, 10, 0, 0, TimeSpan.Zero),
        RevokedBecause = LicenceOutcome.Expired,
        ActivationLimit = 3,
        ActivationUsage = 1,
    };

    [Fact]
    public void Nothing_saved_loads_as_nothing()
    {
        Assert.Null(new DpapiLicenceStore(FilePath).Load());
    }

    [Fact]
    public void A_saved_licence_loads_back_exactly()
    {
        new DpapiLicenceStore(FilePath).Save(Sample);

        Assert.Equal(Sample, new DpapiLicenceStore(FilePath).Load());
    }

    [Fact]
    public void The_file_is_encrypted_so_the_key_cannot_be_read_from_it()
    {
        new DpapiLicenceStore(FilePath).Save(Sample);

        var raw = File.ReadAllText(FilePath);
        Assert.DoesNotContain("38B1460A", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("instance", raw, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_tampered_file_is_refused()
    {
        new DpapiLicenceStore(FilePath).Save(Sample);
        var bytes = File.ReadAllBytes(FilePath);
        bytes[^5] ^= 0xFF;
        File.WriteAllBytes(FilePath, bytes);

        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => new DpapiLicenceStore(FilePath).Load());
    }

    [Fact]
    public void Delete_removes_it()
    {
        var store = new DpapiLicenceStore(FilePath);
        store.Save(Sample);

        store.Delete();

        Assert.Null(store.Load());
        store.Delete();
    }
}
