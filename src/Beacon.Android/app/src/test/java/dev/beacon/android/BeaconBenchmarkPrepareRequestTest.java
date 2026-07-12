package dev.beacon.android;

import org.junit.Test;

import java.lang.reflect.Field;
import java.util.Arrays;
import java.util.Locale;
import java.util.stream.Collectors;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertThrows;
import static org.junit.Assert.assertTrue;

public final class BeaconBenchmarkPrepareRequestTest {
    private static final String RAW_SSID = "private-wifi-name";
    private static final String RAW_BSSID = "00:11:22:33:44:55";

    @Test
    public void requestSerializationContainsOnlySaltedNetworkIdentityHash() {
        RecordingSaltStorage storage = new RecordingSaltStorage();
        BeaconBenchmarkPrepareRequest request = request(storage);

        String explicitJson = request.serialize();
        String gsonJson = BeaconJson.gson().toJson(request);

        assertPrivate(explicitJson);
        assertPrivate(gsonJson);
        assertPrivate(request.toString());
        assertPrivate(request.fingerprints().network().toString());
        assertFalse(explicitJson.contains(storage.encodedSalt));
        assertTrue(explicitJson.contains("\"saltedNetworkIdHash\""));
        assertTrue(explicitJson.matches(".*\"saltedNetworkIdHash\":\"[0-9a-f]{64}\".*"));
    }

    @Test
    public void serverFacingNetworkDtoExposesNoRawNameFields() {
        String fieldNames = Arrays.stream(
                BeaconBenchmarkPrepareRequest.NetworkFingerprint.class.getDeclaredFields())
            .map(Field::getName)
            .collect(Collectors.joining(","))
            .toLowerCase(Locale.ROOT);

        assertFalse(fieldNames.contains("ssid"));
        assertFalse(fieldNames.contains("bssid"));
        assertTrue(fieldNames.contains("saltednetworkidhash"));
    }

    @Test
    public void stableLocalSaltProducesStableRequestFingerprint() {
        RecordingSaltStorage storage = new RecordingSaltStorage();

        String firstHash = request(storage).fingerprints().network().saltedNetworkIdHash();
        String secondHash = request(storage).fingerprints().network().saltedNetworkIdHash();

        assertEquals(firstHash, secondHash);
        assertEquals(1, storage.writeCount);
    }

    @Test
    public void fingerprintsRequireTheServerOwnedSchemaVersion() {
        RecordingSaltStorage storage = new RecordingSaltStorage();
        BeaconNetworkIdentityHasher hasher = hasher(storage);

        assertThrows(
            IllegalArgumentException.class,
            () -> BeaconBenchmarkPrepareRequest.NetworkFingerprint.fromLocalNetwork(
                4,
                "192.168.1.10",
                "wifi",
                "192.168.1.0/24",
                "6-ghz",
                37,
                "500-999-mbps",
                RAW_SSID,
                RAW_BSSID,
                hasher));
        assertThrows(
            IllegalArgumentException.class,
            () -> new BeaconBenchmarkPrepareRequest.HardwareFingerprint(
                4,
                "caps-a",
                "16",
                "1.0.0",
                "display-a",
                "codec-a"));
    }

    private static BeaconBenchmarkPrepareRequest request(RecordingSaltStorage storage) {
        BeaconNetworkIdentityHasher hasher = hasher(storage);
        BeaconBenchmarkPrepareRequest.NetworkFingerprint network =
            BeaconBenchmarkPrepareRequest.NetworkFingerprint.fromLocalNetwork(
                3,
                "192.168.1.10",
                "wifi",
                "192.168.1.0/24",
                "6-ghz",
                37,
                "500-999-mbps",
                RAW_SSID,
                RAW_BSSID,
                hasher);
        BeaconBenchmarkPrepareRequest.HardwareFingerprint hardware =
            new BeaconBenchmarkPrepareRequest.HardwareFingerprint(
                3,
                "caps-a",
                "16",
                "1.0.0",
                "display-a",
                "codec-a");
        return new BeaconBenchmarkPrepareRequest(
            "automatic",
            new BeaconBenchmarkPrepareRequest.FingerprintSet(network, hardware));
    }

    private static BeaconNetworkIdentityHasher hasher(RecordingSaltStorage storage) {
        return new BeaconNetworkIdentityHasher(
            storage,
            () -> {
                byte[] salt = new byte[32];
                Arrays.fill(salt, (byte) 0x5a);
                return salt;
            });
    }

    private static void assertPrivate(String value) {
        String lower = value.toLowerCase(Locale.ROOT);
        assertFalse(value.contains(RAW_SSID));
        assertFalse(value.contains(RAW_BSSID));
        assertFalse(lower.contains("ssid"));
        assertFalse(lower.contains("bssid"));
    }

    private static final class RecordingSaltStorage implements BeaconNetworkIdentityHasher.SaltStorage {
        String encodedSalt = "";
        int writeCount;

        @Override
        public String read() {
            return encodedSalt;
        }

        @Override
        public boolean write(String encodedSalt) {
            this.encodedSalt = encodedSalt;
            writeCount++;
            return true;
        }
    }
}
