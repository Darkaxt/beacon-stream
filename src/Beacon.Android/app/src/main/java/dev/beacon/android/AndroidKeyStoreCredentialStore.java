package dev.beacon.android;

import android.content.Context;
import android.content.SharedPreferences;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

public final class AndroidKeyStoreCredentialStore implements BeaconCredentialStore {
    private static final String KEY_STORE = "AndroidKeyStore";
    private static final String VALUE_KEY = "wrapped_client_credential";
    private final SharedPreferences preferences;
    private final String keyAlias;

    public AndroidKeyStoreCredentialStore(Context context, String clientId) {
        preferences = context.getSharedPreferences("beacon_credentials", Context.MODE_PRIVATE);
        keyAlias = "beacon.client." + clientId;
    }

    @Override
    public String loadCredential() {
        String encoded = preferences.getString(VALUE_KEY + "." + keyAlias, null);
        if (encoded == null || encoded.isEmpty()) {
            return null;
        }
        try {
            byte[] payload = Base64.decode(encoded, Base64.NO_WRAP);
            int ivLength = payload[0] & 0xff;
            byte[] iv = new byte[ivLength];
            byte[] encrypted = new byte[payload.length - ivLength - 1];
            System.arraycopy(payload, 1, iv, 0, ivLength);
            System.arraycopy(payload, ivLength + 1, encrypted, 0, encrypted.length);
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.DECRYPT_MODE, getOrCreateKey(), new GCMParameterSpec(128, iv));
            return new String(cipher.doFinal(encrypted), StandardCharsets.UTF_8);
        } catch (Exception error) {
            throw new IllegalStateException("Beacon client credential could not be unwrapped.", error);
        }
    }

    @Override
    public void saveCredential(String credential) {
        if (credential == null || credential.trim().isEmpty()) {
            throw new IllegalArgumentException("Beacon client credential is required.");
        }
        try {
            Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
            cipher.init(Cipher.ENCRYPT_MODE, getOrCreateKey());
            byte[] iv = cipher.getIV();
            byte[] encrypted = cipher.doFinal(credential.getBytes(StandardCharsets.UTF_8));
            byte[] payload = new byte[1 + iv.length + encrypted.length];
            payload[0] = (byte) iv.length;
            System.arraycopy(iv, 0, payload, 1, iv.length);
            System.arraycopy(encrypted, 0, payload, iv.length + 1, encrypted.length);
            preferences.edit()
                .putString(VALUE_KEY + "." + keyAlias, Base64.encodeToString(payload, Base64.NO_WRAP))
                .apply();
        } catch (Exception error) {
            throw new IllegalStateException("Beacon client credential could not be wrapped.", error);
        }
    }

    @Override
    public void clearCredential() {
        preferences.edit().remove(VALUE_KEY + "." + keyAlias).apply();
        try {
            KeyStore keyStore = KeyStore.getInstance(KEY_STORE);
            keyStore.load(null);
            keyStore.deleteEntry(keyAlias);
        } catch (Exception error) {
            throw new IllegalStateException("Beacon client credential key could not be removed.", error);
        }
    }

    private SecretKey getOrCreateKey() throws Exception {
        KeyStore keyStore = KeyStore.getInstance(KEY_STORE);
        keyStore.load(null);
        KeyStore.Entry existing = keyStore.getEntry(keyAlias, null);
        if (existing instanceof KeyStore.SecretKeyEntry) {
            return ((KeyStore.SecretKeyEntry) existing).getSecretKey();
        }

        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, KEY_STORE);
        generator.init(new KeyGenParameterSpec.Builder(
            keyAlias,
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .build());
        return generator.generateKey();
    }
}
