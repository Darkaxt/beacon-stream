package dev.beacon.android;

import org.junit.Test;

import java.util.Arrays;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

public final class AndroidBenchmarkFingerprintProbeTest {
    @Test
    public void buildsVersionedFactsWithoutExposingRawWifiIdentity() {
        byte[] salt = new byte[32];
        Arrays.fill(salt, (byte) 0x5a);
        BeaconNetworkIdentityHasher hasher = new BeaconNetworkIdentityHasher(
            new MemorySaltStorage(),
            () -> salt.clone());
        AndroidBenchmarkFingerprintProbe probe = new AndroidBenchmarkFingerprintProbe(
            hasher,
            capabilities -> new AndroidBenchmarkFingerprintProbe.HardwareFacts(
                "device-revision",
                "16",
                "0.1.0",
                "display-revision",
                "codec-revision"));
        AndroidBenchmarkNetworkState network = new AndroidBenchmarkNetworkState(
            "wifi",
            "192.168.1.0/24",
            "6-ghz",
            37,
            "500-999-mbps",
            "Private SSID",
            "aa:bb:cc:dd:ee:ff");

        BeaconBenchmarkPrepareRequest request = probe.create(
            "automatic",
            "https://192.168.1.10:5001",
            capabilities(),
            network);
        String serialized = request.serialize();

        assertTrue(serialized.contains("\"trigger\":\"automatic\""));
        assertTrue(serialized.contains("\"serverRoute\":\"192.168.1.10\""));
        assertFalse(serialized.contains("Private SSID"));
        assertFalse(serialized.contains("aa:bb:cc:dd:ee:ff"));
        assertEquals(64, request.fingerprints().network().saltedNetworkIdHash().length());
    }

    @Test
    public void derivesWifiBandsChannelsAndLinkBucketsDeterministically() {
        assertEquals("2.4-ghz", AndroidBenchmarkFingerprintProbe.wifiBand(2412));
        assertEquals(Integer.valueOf(1), AndroidBenchmarkFingerprintProbe.wifiChannel(2412));
        assertEquals("5-ghz", AndroidBenchmarkFingerprintProbe.wifiBand(5745));
        assertEquals(Integer.valueOf(149), AndroidBenchmarkFingerprintProbe.wifiChannel(5745));
        assertEquals("6-ghz", AndroidBenchmarkFingerprintProbe.wifiBand(6135));
        assertEquals(Integer.valueOf(37), AndroidBenchmarkFingerprintProbe.wifiChannel(6135));
        assertNull(AndroidBenchmarkFingerprintProbe.wifiChannel(0));
        assertEquals("unknown", AndroidBenchmarkFingerprintProbe.linkSpeedBucket(0));
        assertEquals("500-999-mbps", AndroidBenchmarkFingerprintProbe.linkSpeedBucket(866));
        assertEquals("1000+-mbps", AndroidBenchmarkFingerprintProbe.linkSpeedBucket(1200));
    }

    private static BeaconApiClient.ClientCapabilities capabilities() {
        return new BeaconApiClient.ClientCapabilities(
            false, false, true, false, false, 60, true, "1280x720@60");
    }

    private static final class MemorySaltStorage
        implements BeaconNetworkIdentityHasher.SaltStorage {
        private String value = "";

        @Override public String read() { return value; }
        @Override public boolean write(String encodedSalt) {
            value = encodedSalt;
            return true;
        }
    }
}
