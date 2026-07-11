package dev.beacon.android;

import java.security.GeneralSecurityException;
import java.security.MessageDigest;
import java.security.cert.CertificateException;
import java.security.cert.X509Certificate;

import javax.net.ssl.SSLContext;
import javax.net.ssl.SSLSocketFactory;
import javax.net.ssl.TrustManager;
import javax.net.ssl.X509TrustManager;

final class BeaconPinnedTrust {
    private BeaconPinnedTrust() { }

    static SSLSocketFactory createSocketFactory(String expectedFingerprint) {
        try {
            X509TrustManager pinned = new PinnedTrustManager(hex(expectedFingerprint));
            SSLContext context = SSLContext.getInstance("TLS");
            context.init(null, new TrustManager[] { pinned }, null);
            return context.getSocketFactory();
        } catch (GeneralSecurityException error) {
            throw new IllegalStateException("Beacon pinned TLS could not be initialized.", error);
        }
    }

    static void verifyPublicKey(byte[] encodedPublicKey, byte[] expectedFingerprint)
        throws CertificateException {
        try {
            byte[] actual = MessageDigest.getInstance("SHA-256").digest(encodedPublicKey);
            if (!MessageDigest.isEqual(actual, expectedFingerprint)) {
                throw new CertificateException("Beacon server public-key pin mismatch.");
            }
        } catch (GeneralSecurityException error) {
            throw new CertificateException("Beacon server public-key pin could not be verified.", error);
        }
    }

    private static byte[] hex(String value) {
        byte[] output = new byte[value.length() / 2];
        for (int index = 0; index < output.length; index++) {
            output[index] = (byte) Integer.parseInt(value.substring(index * 2, index * 2 + 2), 16);
        }
        return output;
    }

    private static final class PinnedTrustManager implements X509TrustManager {
        private final byte[] expectedFingerprint;

        PinnedTrustManager(byte[] expectedFingerprint) {
            this.expectedFingerprint = expectedFingerprint;
        }

        @Override
        public void checkClientTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            throw new CertificateException("Beacon does not accept TLS client certificates.");
        }

        @Override
        public void checkServerTrusted(X509Certificate[] chain, String authType) throws CertificateException {
            if (chain == null || chain.length == 0) {
                throw new CertificateException("Beacon server certificate chain is empty.");
            }
            verifyPublicKey(chain[0].getPublicKey().getEncoded(), expectedFingerprint);
        }

        @Override
        public X509Certificate[] getAcceptedIssuers() {
            return new X509Certificate[0];
        }
    }
}
