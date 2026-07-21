using System.Reflection;

namespace Beacon.HostAgent.Update;

public static class HostAgentUpdateTrust
{
    private const string PublicKeyResource = "Beacon.HostAgent.Update.PublicKey.pem";

    public static string PublicKeyPem { get; } = LoadPublicKey();

    private static string LoadPublicKey()
    {
        using Stream stream = typeof(HostAgentUpdateTrust).Assembly.GetManifestResourceStream(
            PublicKeyResource)
            ?? throw new InvalidOperationException("Host Agent update public key is not embedded.");
        using var reader = new StreamReader(stream);
        string value = reader.ReadToEnd();
        return value.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal)
            ? value
            : throw new InvalidDataException("Embedded Host Agent update public key is invalid.");
    }
}
