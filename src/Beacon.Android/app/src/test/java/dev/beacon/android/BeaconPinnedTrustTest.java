package dev.beacon.android;

import org.junit.Test;

import java.security.MessageDigest;
import java.security.cert.CertificateException;

import static org.junit.Assert.assertThrows;

public final class BeaconPinnedTrustTest {
    @Test
    public void matchingPublicKeyFingerprintIsAccepted() throws Exception {
        byte[] publicKey = new byte[] { 1, 2, 3, 4 };

        BeaconPinnedTrust.verifyPublicKey(
            publicKey,
            MessageDigest.getInstance("SHA-256").digest(publicKey));
    }

    @Test
    public void publicKeyPinMismatchFailsClosed() {
        assertThrows(
            CertificateException.class,
            () -> BeaconPinnedTrust.verifyPublicKey(new byte[] { 1 }, new byte[32]));
    }
}
