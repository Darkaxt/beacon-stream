package dev.beacon.android;

import android.content.Context;
import android.content.SharedPreferences;

import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.security.GeneralSecurityException;
import java.security.SecureRandom;
import java.util.Arrays;
import java.util.Base64;
import java.util.Locale;

import javax.crypto.Mac;
import javax.crypto.spec.SecretKeySpec;

public final class BeaconNetworkIdentityHasher {
    private static final String PREFERENCES_NAME = "beacon_network_privacy";
    private static final String SALT_KEY = "network_identity_salt_v1";
    private static final byte[] HASH_DOMAIN =
        "beacon-network-identity-v1".getBytes(StandardCharsets.US_ASCII);
    private static final int SALT_BYTES = 32;
    private static final Object SALT_LOCK = new Object();
    private static final SecureRandom SECURE_RANDOM = new SecureRandom();

    private final SaltStorage storage;
    private final SaltGenerator saltGenerator;

    public static BeaconNetworkIdentityHasher system(Context context) {
        Context applicationContext = context.getApplicationContext();
        Context storageContext = applicationContext == null ? context : applicationContext;
        SharedPreferences preferences = storageContext.getSharedPreferences(
            PREFERENCES_NAME,
            Context.MODE_PRIVATE);
        return new BeaconNetworkIdentityHasher(
            new SharedPreferencesSaltStorage(preferences),
            BeaconNetworkIdentityHasher::randomSalt);
    }

    BeaconNetworkIdentityHasher(SaltStorage storage, SaltGenerator saltGenerator) {
        if (storage == null || saltGenerator == null) {
            throw new IllegalArgumentException("Network identity salt dependencies are required.");
        }
        this.storage = storage;
        this.saltGenerator = saltGenerator;
    }

    public String hash(String rawSsid, String rawBssid) {
        String canonicalBssid = canonicalBssid(rawBssid);
        if (isUnavailable(rawSsid) && canonicalBssid == null) {
            return null;
        }

        byte[] salt = loadOrCreateSalt();
        try {
            Mac mac = Mac.getInstance("HmacSHA256");
            mac.init(new SecretKeySpec(salt, "HmacSHA256"));
            mac.update(HASH_DOMAIN);
            updatePart(mac, isUnavailable(rawSsid) ? null : rawSsid);
            updatePart(mac, canonicalBssid);
            return lowerHex(mac.doFinal());
        } catch (GeneralSecurityException error) {
            throw new IllegalStateException("Could not hash the local network identity.", error);
        } finally {
            Arrays.fill(salt, (byte) 0);
        }
    }

    private byte[] loadOrCreateSalt() {
        synchronized (SALT_LOCK) {
            byte[] stored = decodeSalt(storage.read());
            if (stored != null) {
                return stored;
            }

            byte[] generated = saltGenerator.create();
            if (generated == null || generated.length != SALT_BYTES) {
                if (generated != null) {
                    Arrays.fill(generated, (byte) 0);
                }
                throw new IllegalStateException("The local network identity salt generator returned invalid data.");
            }

            String encoded = Base64.getEncoder().withoutPadding().encodeToString(generated);
            if (!storage.write(encoded)) {
                Arrays.fill(generated, (byte) 0);
                throw new IllegalStateException("Could not persist the local network identity salt.");
            }
            return generated;
        }
    }

    private static byte[] decodeSalt(String encoded) {
        if (encoded == null || encoded.isEmpty()) {
            return null;
        }

        try {
            byte[] decoded = Base64.getDecoder().decode(encoded);
            if (decoded.length == SALT_BYTES) {
                return decoded;
            }
            Arrays.fill(decoded, (byte) 0);
        } catch (IllegalArgumentException ignored) {
            // Invalid local state is replaced with a fresh salt below.
        }
        return null;
    }

    private static void updatePart(Mac mac, String value) {
        if (value == null) {
            mac.update((byte) 0);
            return;
        }

        byte[] bytes = value.getBytes(StandardCharsets.UTF_8);
        try {
            mac.update((byte) 1);
            mac.update(ByteBuffer.allocate(Integer.BYTES).putInt(bytes.length).array());
            mac.update(bytes);
        } finally {
            Arrays.fill(bytes, (byte) 0);
        }
    }

    private static String canonicalBssid(String value) {
        if (isUnavailable(value)) {
            return null;
        }
        return value.trim().toLowerCase(Locale.ROOT);
    }

    private static boolean isUnavailable(String value) {
        return value == null || value.isEmpty();
    }

    private static String lowerHex(byte[] bytes) {
        char[] chars = new char[bytes.length * 2];
        char[] alphabet = "0123456789abcdef".toCharArray();
        for (int index = 0; index < bytes.length; index++) {
            int value = bytes[index] & 0xff;
            chars[index * 2] = alphabet[value >>> 4];
            chars[index * 2 + 1] = alphabet[value & 0x0f];
        }
        return new String(chars);
    }

    private static byte[] randomSalt() {
        byte[] salt = new byte[SALT_BYTES];
        SECURE_RANDOM.nextBytes(salt);
        return salt;
    }

    interface SaltStorage {
        String read();
        boolean write(String encodedSalt);
    }

    interface SaltGenerator {
        byte[] create();
    }

    private static final class SharedPreferencesSaltStorage implements SaltStorage {
        private final SharedPreferences preferences;

        SharedPreferencesSaltStorage(SharedPreferences preferences) {
            this.preferences = preferences;
        }

        @Override
        public String read() {
            return preferences.getString(SALT_KEY, "");
        }

        @Override
        public boolean write(String encodedSalt) {
            return preferences.edit().putString(SALT_KEY, encodedSalt).commit();
        }
    }
}
