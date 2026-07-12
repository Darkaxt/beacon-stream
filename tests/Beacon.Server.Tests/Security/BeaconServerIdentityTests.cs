using Beacon.Server.Security;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

namespace Beacon.Server.Tests.Security;

public sealed class BeaconServerIdentityTests
{
    [Fact]
    public async Task ConcurrentCreationLoadsTheSinglePersistedIdentity()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beacon-identity-{Guid.NewGuid():N}.pfx");
        const int participantCount = 8;
        using var start = new Barrier(participantCount + 1);

        try
        {
            Task<string>[] creations = Enumerable.Range(0, participantCount)
                .Select(_ => Task.Run(() =>
                {
                    start.SignalAndWait();
                    using var identity = new BeaconServerIdentity(path);
                    return identity.PublicKeyFingerprint;
                }))
                .ToArray();
            start.SignalAndWait();

            string[] fingerprints = await Task.WhenAll(creations);

            Assert.Single(fingerprints.Distinct(StringComparer.Ordinal));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IdentityPersistsCertificateAndPublicKeyFingerprint()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beacon-identity-{Guid.NewGuid():N}.pfx");
        try
        {
            using var first = new BeaconServerIdentity(path);
            string fingerprint = first.PublicKeyFingerprint;

            using var second = new BeaconServerIdentity(path);

            Assert.True(File.Exists(path));
            Assert.True(first.Certificate.HasPrivateKey);
            Assert.True(second.Certificate.HasPrivateKey);
            Assert.Equal(fingerprint, second.PublicKeyFingerprint);
            Assert.Equal(64, Convert.FromHexString(fingerprint).Length * 2);
            using RSA? privateKey = first.Certificate.GetRSAPrivateKey();
            Assert.NotNull(privateKey);
            Assert.DoesNotContain(Convert.ToBase64String(privateKey.ExportPkcs8PrivateKey()), first.ToString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CorruptIdentityFailsClosedInsteadOfReplacingTrustAnchor()
    {
        string path = Path.Combine(Path.GetTempPath(), $"beacon-identity-{Guid.NewGuid():N}.pfx");
        try
        {
            File.WriteAllText(path, "not-a-certificate");

            Assert.Throws<BeaconIdentityException>(() => new BeaconServerIdentity(path));
            Assert.Equal("not-a-certificate", File.ReadAllText(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
