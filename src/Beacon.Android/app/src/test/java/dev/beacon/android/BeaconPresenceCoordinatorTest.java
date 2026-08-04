package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNull;
import static org.junit.Assert.assertTrue;

public final class BeaconPresenceCoordinatorTest {
    private static final String FINGERPRINT_A =
        "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
    private static final String FINGERPRINT_B =
        "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB";

    @Test
    public void firstRunWaitsForExplicitConnection() {
        FakeStorage storage = new FakeStorage();
        BeaconPresenceCoordinator coordinator = coordinator(storage);

        assertNull(coordinator.enrolledConfig());
        assertNull(coordinator.onForeground());

        BeaconClientConfig explicit = config("https://first.example", "first", FINGERPRINT_A);
        BeaconPresenceCoordinator.ActivationAttempt attempt =
            coordinator.beginExplicitActivation(explicit);
        assertTrue(coordinator.activationSucceeded(attempt));

        assertAttemptConfig(coordinator(storage).onForeground(), explicit);
    }

    @Test
    public void savedEnrollmentHydratesAndActivatesOncePerForeground() {
        FakeStorage storage = enrolledStorage(
            config("https://saved.example/", "saved-client", FINGERPRINT_A));
        BeaconPresenceCoordinator coordinator = coordinator(storage);

        assertConfig(coordinator.enrolledConfig(),
            config("https://saved.example", "saved-client", FINGERPRINT_A));
        assertAttemptConfig(coordinator.onForeground(), coordinator.enrolledConfig());
        assertNull(coordinator.onForeground());
        assertConfig(coordinator.onBackground(), coordinator.enrolledConfig());
        assertNull(coordinator.onBackground());
        assertAttemptConfig(coordinator.onForeground(), coordinator.enrolledConfig());
        assertNull(coordinator.onForeground());
    }

    @Test
    public void failedEditedActivationDoesNotReplaceSavedEnrollment() {
        BeaconClientConfig saved = config("https://saved.example", "saved", FINGERPRINT_A);
        BeaconClientConfig edited = config("https://edited.example", "edited", FINGERPRINT_B);
        FakeStorage storage = enrolledStorage(saved);
        BeaconPresenceCoordinator coordinator = coordinator(storage);

        coordinator.onForeground();
        BeaconPresenceCoordinator.ActivationAttempt attempt =
            coordinator.beginExplicitActivation(edited);
        assertTrue(coordinator.activationFailed(attempt));
        assertConfig(coordinator.onBackground(), edited);

        assertConfig(coordinator(storage).enrolledConfig(), saved);
        assertAttemptConfig(coordinator.onForeground(), saved);
    }

    @Test
    public void successfullyActivatedEditReplacesSavedEnrollment() {
        BeaconClientConfig saved = config("https://saved.example", "saved", FINGERPRINT_A);
        BeaconClientConfig edited = config("https://edited.example/", "edited", FINGERPRINT_B);
        FakeStorage storage = enrolledStorage(saved);
        BeaconPresenceCoordinator coordinator = coordinator(storage);

        BeaconPresenceCoordinator.ActivationAttempt attempt =
            coordinator.beginExplicitActivation(edited);
        assertTrue(coordinator.activationSucceeded(attempt));

        assertConfig(coordinator(storage).enrolledConfig(),
            config("https://edited.example", "edited", FINGERPRINT_B));
    }

    @Test
    public void explicitQuitStopsCurrentAutomaticPresenceWithoutErasingEnrollment() {
        BeaconClientConfig saved = config("https://saved.example", "saved", FINGERPRINT_A);
        FakeStorage storage = enrolledStorage(saved);
        BeaconPresenceCoordinator coordinator = coordinator(storage);

        assertAttemptConfig(coordinator.onForeground(), saved);
        coordinator.explicitQuit();

        assertNull(coordinator.onBackground());
        assertNull(coordinator.onForeground());
        assertAttemptConfig(coordinator(storage).onForeground(), saved);
    }

    @Test
    public void supersededActivationResultsAreIgnoredUntilCurrentAttemptSucceeds() {
        BeaconClientConfig first = config("https://first.example", "first", FINGERPRINT_A);
        BeaconClientConfig second = config("https://second.example", "second", FINGERPRINT_B);
        FakeStorage storage = new FakeStorage();
        BeaconPresenceCoordinator coordinator = coordinator(storage);

        BeaconPresenceCoordinator.ActivationAttempt firstAttempt =
            coordinator.beginExplicitActivation(first);
        BeaconPresenceCoordinator.ActivationAttempt secondAttempt =
            coordinator.beginExplicitActivation(second);

        assertFalse(coordinator.activationSucceeded(firstAttempt));
        assertFalse(coordinator.activationFailed(firstAttempt));
        assertEquals(0, storage.writeCount);
        assertNull(coordinator(storage).enrolledConfig());
        assertConfig(coordinator.onBackground(), second);

        assertTrue(coordinator.activationSucceeded(secondAttempt));
        assertEquals(1, storage.writeCount);
        assertConfig(coordinator(storage).enrolledConfig(), second);
    }

    private static BeaconPresenceCoordinator coordinator(FakeStorage storage) {
        return new BeaconPresenceCoordinator(new BeaconEnrollmentStore(storage));
    }

    private static FakeStorage enrolledStorage(BeaconClientConfig config) {
        FakeStorage storage = new FakeStorage();
        new BeaconEnrollmentStore(storage).save(config);
        return storage;
    }

    private static BeaconClientConfig config(String serverUrl, String clientId, String fingerprint) {
        return new BeaconClientConfig(serverUrl, clientId, fingerprint);
    }

    private static void assertConfig(BeaconClientConfig actual, BeaconClientConfig expected) {
        assertEquals(expected.serverUrl(), actual.serverUrl());
        assertEquals(expected.clientId(), actual.clientId());
        assertEquals(expected.publicKeyFingerprint(), actual.publicKeyFingerprint());
    }

    private static void assertAttemptConfig(
        BeaconPresenceCoordinator.ActivationAttempt attempt,
        BeaconClientConfig expected) {
        assertConfig(attempt.config(), expected);
    }

    private static final class FakeStorage implements BeaconEnrollmentStorage {
        private String json = "";
        private int writeCount;

        @Override
        public String read() {
            return json;
        }

        @Override
        public void write(String json) {
            this.json = json;
            writeCount++;
        }
    }
}
