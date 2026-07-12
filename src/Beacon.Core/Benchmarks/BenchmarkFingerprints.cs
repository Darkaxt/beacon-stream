using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Beacon.Core.Benchmarks;

public sealed record NetworkFingerprint(
    int SchemaVersion,
    string ServerRoute,
    string Transport,
    string LocalNetworkPrefix,
    string? WifiBand,
    int? WifiChannel,
    string LinkSpeedBucket,
    string? SaltedNetworkIdHash)
{
    public string Revision => FingerprintRevision.Create(
        SchemaVersion.ToString(CultureInfo.InvariantCulture),
        ServerRoute,
        Transport,
        LocalNetworkPrefix,
        WifiBand,
        WifiChannel?.ToString(CultureInfo.InvariantCulture),
        LinkSpeedBucket,
        SaltedNetworkIdHash);
}

public sealed record HardwareFingerprint(
    int SchemaVersion,
    string DeviceCapabilityRevision,
    string AndroidVersion,
    string ApkVersion,
    string DisplayModeInventoryRevision,
    string CodecInventoryRevision)
{
    public string Revision => FingerprintRevision.Create(
        SchemaVersion.ToString(CultureInfo.InvariantCulture),
        DeviceCapabilityRevision,
        AndroidVersion,
        ApkVersion,
        DisplayModeInventoryRevision,
        CodecInventoryRevision);
}

public sealed record BenchmarkFingerprintSet(
    NetworkFingerprint Network,
    HardwareFingerprint Hardware);

internal static class FingerprintRevision
{
    public static string Create(params string?[] values)
    {
        using var material = new MemoryStream();
        using (var writer = new BinaryWriter(material, Encoding.UTF8, leaveOpen: true))
        {
            foreach (string? value in values)
            {
                writer.Write(value is not null);
                if (value is not null)
                {
                    writer.Write(value);
                }
            }
        }

        byte[] digest = SHA256.HashData(material.GetBuffer().AsSpan(0, checked((int)material.Length)));
        try
        {
            return Convert.ToHexStringLower(digest);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(digest);
        }
    }
}
