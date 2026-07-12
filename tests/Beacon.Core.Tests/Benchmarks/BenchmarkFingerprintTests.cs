using System.Text.Json;
using Beacon.Core.Benchmarks;

namespace Beacon.Core.Tests.Benchmarks;

public sealed class BenchmarkFingerprintTests
{
    [Fact]
    public void NetworkRevisionIsStableAndChangesForEveryMaterialFact()
    {
        NetworkFingerprint baseline = CreateNetworkFingerprint();

        Assert.Equal(baseline.Revision, CreateNetworkFingerprint().Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { SchemaVersion = 4 }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { ServerRoute = "192.168.8.10" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { Transport = "cellular" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { LocalNetworkPrefix = "192.168.8.0/24" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { WifiBand = "5-ghz" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { WifiChannel = 44 }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { LinkSpeedBucket = "1000-plus-mbps" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { SaltedNetworkIdHash = new string('b', 64) }).Revision);
    }

    [Fact]
    public void HardwareRevisionChangesForEveryCalibrationInvalidator()
    {
        HardwareFingerprint baseline = CreateHardwareFingerprint();

        Assert.Equal(baseline.Revision, CreateHardwareFingerprint().Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { SchemaVersion = 4 }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { DeviceCapabilityRevision = "caps-b" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { AndroidVersion = "17" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { ApkVersion = "2.0.0" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { DisplayModeInventoryRevision = "display-b" }).Revision);
        Assert.NotEqual(baseline.Revision, (baseline with { CodecInventoryRevision = "codec-b" }).Revision);
    }

    [Fact]
    public void SerializedFingerprintNeverContainsRawNetworkNames()
    {
        const string rawSsid = "private-wifi-name";
        const string rawBssid = "00:11:22:33:44:55";
        NetworkFingerprint fingerprint = CreateNetworkFingerprint() with
        {
            SaltedNetworkIdHash = "f4b2e83d6f1482020eec78c6a8a99990f4b2e83d6f1482020eec78c6a8a99990"
        };

        string json = JsonSerializer.Serialize(fingerprint);

        Assert.DoesNotContain(rawSsid, json, StringComparison.Ordinal);
        Assert.DoesNotContain(rawBssid, json, StringComparison.Ordinal);
        Assert.DoesNotContain("ssid", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("bssid", json, StringComparison.OrdinalIgnoreCase);
    }

    internal static NetworkFingerprint CreateNetworkFingerprint() =>
        new(
            SchemaVersion: 3,
            ServerRoute: "192.168.1.10",
            Transport: "wifi",
            LocalNetworkPrefix: "192.168.1.0/24",
            WifiBand: "6-ghz",
            WifiChannel: 37,
            LinkSpeedBucket: "500-999-mbps",
            SaltedNetworkIdHash: new string('a', 64));

    internal static HardwareFingerprint CreateHardwareFingerprint() =>
        new(
            SchemaVersion: 3,
            DeviceCapabilityRevision: "caps-a",
            AndroidVersion: "16",
            ApkVersion: "1.0.0",
            DisplayModeInventoryRevision: "display-a",
            CodecInventoryRevision: "codec-a");
}
