using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Beacon.Server.Security;

public sealed class BeaconServerIdentity : IDisposable
{
    private readonly X509Certificate2 certificate;

    public BeaconServerIdentity(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Beacon identity path is required.", nameof(path));
        }

        IdentityPath = Path.GetFullPath(path);
        try
        {
            certificate = File.Exists(IdentityPath)
                ? Load(IdentityPath)
                : Create(IdentityPath);
            (Algorithm, PublicKeyFingerprint) = ComputePublicKeyFingerprint(certificate);
        }
        catch (BeaconIdentityException)
        {
            throw;
        }
        catch (Exception error) when (error is CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new BeaconIdentityException("Beacon server identity could not be loaded.", error);
        }
    }

    public string IdentityPath { get; }

    public X509Certificate2 Certificate => certificate;

    public string PublicKeyFingerprint { get; }

    public string Algorithm { get; }

    public void Dispose() => certificate.Dispose();

    public override string ToString() =>
        $"Beacon server identity {PublicKeyFingerprint} ({Path.GetFileName(IdentityPath)}).";

    private static X509Certificate2 Create(string path)
    {
        string? directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using RSA key = RSA.Create(3072);
        var request = new CertificateRequest(
            "CN=Beacon Stream",
            key,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(
            X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
            true));
        var usages = new OidCollection
        {
            new Oid("1.3.6.1.5.5.7.3.1", "Server Authentication"),
        };
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(usages, true));
        var subjectNames = new SubjectAlternativeNameBuilder();
        subjectNames.AddDnsName("localhost");
        subjectNames.AddIpAddress(System.Net.IPAddress.Loopback);
        subjectNames.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(subjectNames.Build());
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));
        using X509Certificate2 created = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
        byte[] pfx = created.Export(X509ContentType.Pfx);
        string temporaryPath = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporaryPath, pfx);
            File.Move(temporaryPath, path, overwrite: false);
        }
        finally
        {
            File.Delete(temporaryPath);
            CryptographicOperations.ZeroMemory(pfx);
        }
        return Load(path);
    }

    private static X509Certificate2 Load(string path)
    {
        X509Certificate2 loaded = X509CertificateLoader.LoadPkcs12FromFile(
            path,
            password: null,
            X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
        if (!loaded.HasPrivateKey)
        {
            loaded.Dispose();
            throw new BeaconIdentityException("Beacon server identity has no private key.");
        }
        return loaded;
    }

    private static (string Algorithm, string Fingerprint) ComputePublicKeyFingerprint(X509Certificate2 value)
    {
        using RSA? publicKey = value.GetRSAPublicKey();
        if (publicKey is not null)
        {
            return (
                "RSA_3072_SHA256",
                Convert.ToHexString(SHA256.HashData(publicKey.ExportSubjectPublicKeyInfo())));
        }
        using ECDsa? ellipticCurveKey = value.GetECDsaPublicKey();
        if (ellipticCurveKey is not null)
        {
            return (
                "ECDSA_P256_SHA256",
                Convert.ToHexString(SHA256.HashData(ellipticCurveKey.ExportSubjectPublicKeyInfo())));
        }
        throw new BeaconIdentityException("Beacon server identity uses an unsupported public-key algorithm.");
    }
}

public sealed class BeaconIdentityException : Exception
{
    public BeaconIdentityException(string message)
        : base(message)
    {
    }

    public BeaconIdentityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
