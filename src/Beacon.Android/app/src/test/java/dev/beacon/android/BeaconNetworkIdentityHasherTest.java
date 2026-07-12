package dev.beacon.android;

import org.junit.Test;

import java.util.Arrays;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertThrows;

public final class BeaconNetworkIdentityHasherTest {
    @Test
    public void firstUsePersistsSaltAndLaterInstancesReuseIt() {
        FakeSaltStorage storage = new FakeSaltStorage();
        BeaconNetworkIdentityHasher first = new BeaconNetworkIdentityHasher(
            storage,
            () -> bytes(0x31));

        String firstHash = first.hash("private-wifi-name", "00:11:22:33:44:55");
        BeaconNetworkIdentityHasher recreated = new BeaconNetworkIdentityHasher(
            storage,
            () -> { throw new AssertionError("persisted salt should be reused"); });
        String recreatedHash = recreated.hash("private-wifi-name", "00:11:22:33:44:55");

        assertEquals(1, storage.writeCount);
        assertFalse(storage.encodedSalt.isEmpty());
        assertEquals(firstHash, recreatedHash);
        assertEquals(64, firstHash.length());
    }

    @Test
    public void malformedPersistedSaltIsReplacedBeforeHashing() {
        FakeSaltStorage storage = new FakeSaltStorage();
        storage.encodedSalt = "not-a-valid-salt";
        BeaconNetworkIdentityHasher hasher = new BeaconNetworkIdentityHasher(
            storage,
            () -> bytes(0x52));

        String firstHash = hasher.hash("private-wifi-name", "00:11:22:33:44:55");
        String persisted = storage.encodedSalt;
        BeaconNetworkIdentityHasher recreated = new BeaconNetworkIdentityHasher(
            storage,
            () -> { throw new AssertionError("replacement salt should be reused"); });

        assertEquals(1, storage.writeCount);
        assertNotEquals("not-a-valid-salt", persisted);
        assertEquals(firstHash, recreated.hash("private-wifi-name", "00:11:22:33:44:55"));
    }

    @Test
    public void failedSaltPersistenceStopsFingerprintCreation() {
        FakeSaltStorage storage = new FakeSaltStorage();
        storage.acceptWrites = false;
        byte[] generatedSalt = bytes(0x63);
        BeaconNetworkIdentityHasher hasher = new BeaconNetworkIdentityHasher(
            storage,
            () -> generatedSalt);

        IllegalStateException error = assertThrows(
            IllegalStateException.class,
            () -> hasher.hash("private-wifi-name", "00:11:22:33:44:55"));

        assertEquals("Could not persist the local network identity salt.", error.getMessage());
        assertArrayEquals(new byte[32], generatedSalt);
    }

    @Test
    public void throwingSaltPersistenceZeroesGeneratedSalt() {
        byte[] generatedSalt = bytes(0x64);
        IllegalStateException persistenceFailure = new IllegalStateException("injected write failure");
        BeaconNetworkIdentityHasher hasher = new BeaconNetworkIdentityHasher(
            new ThrowingSaltStorage(persistenceFailure),
            () -> generatedSalt);

        IllegalStateException error = assertThrows(
            IllegalStateException.class,
            () -> hasher.hash("private-wifi-name", "00:11:22:33:44:55"));

        assertEquals(persistenceFailure, error);
        assertArrayEquals(new byte[32], generatedSalt);
    }

    @Test
    public void differentInstallSaltsProduceDifferentHashes() {
        BeaconNetworkIdentityHasher firstInstall = new BeaconNetworkIdentityHasher(
            new FakeSaltStorage(),
            () -> bytes(0x14));
        BeaconNetworkIdentityHasher secondInstall = new BeaconNetworkIdentityHasher(
            new FakeSaltStorage(),
            () -> bytes(0x72));

        assertNotEquals(
            firstInstall.hash("private-wifi-name", "00:11:22:33:44:55"),
            secondInstall.hash("private-wifi-name", "00:11:22:33:44:55"));
    }

    @Test
    public void identifierPartsAreFramedAndBssidIsCanonicalized() {
        FakeSaltStorage storage = new FakeSaltStorage();
        BeaconNetworkIdentityHasher hasher = new BeaconNetworkIdentityHasher(
            storage,
            () -> bytes(0x27));

        assertNotEquals(hasher.hash("ab", "c"), hasher.hash("a", "bc"));
        assertEquals(
            hasher.hash("private-wifi-name", "AA:BB:CC:DD:EE:FF"),
            hasher.hash("private-wifi-name", "aa:bb:cc:dd:ee:ff"));
    }

    @Test
    public void unavailableIdentifiersDoNotCreateSaltOrHash() {
        FakeSaltStorage storage = new FakeSaltStorage();
        BeaconNetworkIdentityHasher hasher = new BeaconNetworkIdentityHasher(
            storage,
            () -> bytes(0x44));

        assertNull(hasher.hash(null, null));
        assertNull(hasher.hash("", ""));
        assertEquals(0, storage.writeCount);
    }

    private static byte[] bytes(int value) {
        byte[] bytes = new byte[32];
        Arrays.fill(bytes, (byte) value);
        return bytes;
    }

    private static final class FakeSaltStorage implements BeaconNetworkIdentityHasher.SaltStorage {
        String encodedSalt = "";
        int writeCount;
        boolean acceptWrites = true;

        @Override
        public String read() {
            return encodedSalt;
        }

        @Override
        public boolean write(String encodedSalt) {
            writeCount++;
            if (acceptWrites) {
                this.encodedSalt = encodedSalt;
            }
            return acceptWrites;
        }
    }

    private static final class ThrowingSaltStorage implements BeaconNetworkIdentityHasher.SaltStorage {
        private final RuntimeException failure;

        ThrowingSaltStorage(RuntimeException failure) {
            this.failure = failure;
        }

        @Override
        public String read() {
            return "";
        }

        @Override
        public boolean write(String encodedSalt) {
            throw failure;
        }
    }
}
