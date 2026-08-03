package dev.beacon.android;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;

public final class BeaconEnrollmentStoreTest {
    private static final String FINGERPRINT =
        "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF";

    @Test
    public void emptyStorageHasNoEnrollment() {
        BeaconEnrollmentStore store = new BeaconEnrollmentStore(new FakeStorage());

        assertNull(store.load());
    }

    @Test
    public void savePersistsOnlyNormalizedNonSecretConnectionConfiguration() {
        FakeStorage storage = new FakeStorage();
        BeaconEnrollmentStore store = new BeaconEnrollmentStore(storage);
        BeaconClientConfig config = new BeaconClientConfig(
            " https://beacon.example.test/// ",
            " z-fold-7 ",
            "00:11:22:33:44:55:66:77:88:99:aa:bb:cc:dd:ee:ff:" +
                "00:11:22:33:44:55:66:77:88:99:aa:bb:cc:dd:ee:ff");

        store.save(config);

        JsonObject json = JsonParser.parseString(storage.json).getAsJsonObject();
        assertEquals(3, json.size());
        assertEquals("https://beacon.example.test", json.get("serverUrl").getAsString());
        assertEquals("z-fold-7", json.get("clientId").getAsString());
        assertEquals(FINGERPRINT, json.get("publicKeyFingerprint").getAsString());
        assertFalse(storage.json.toLowerCase().contains("credential"));
        assertFalse(storage.json.toLowerCase().contains("private"));

        assertConfig(store.load(), "https://beacon.example.test", "z-fold-7", FINGERPRINT);
    }

    @Test
    public void invalidStoredConfigurationIsIgnored() {
        FakeStorage storage = new FakeStorage();
        storage.json = "{\"serverUrl\":\"http://not-production\",\"clientId\":\"client\"," +
            "\"publicKeyFingerprint\":\"" + FINGERPRINT + "\"}";

        assertNull(new BeaconEnrollmentStore(storage).load());
    }

    private static void assertConfig(
        BeaconClientConfig config,
        String serverUrl,
        String clientId,
        String fingerprint) {
        assertEquals(serverUrl, config.serverUrl());
        assertEquals(clientId, config.clientId());
        assertEquals(fingerprint, config.publicKeyFingerprint());
    }

    private static final class FakeStorage implements BeaconEnrollmentStorage {
        private String json = "";

        @Override
        public String read() {
            return json;
        }

        @Override
        public void write(String json) {
            this.json = json;
        }
    }
}
