package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotSame;
import static org.junit.Assert.assertSame;

public final class BeaconViewModelSessionTest {
    @Test
    public void sameIdentityReusesModel() {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);

        BeaconViewModel first = session.get("z-fold-7", "http://server");
        BeaconViewModel second = session.get("z-fold-7", "http://server");

        assertSame(first, second);
        assertEquals(1, factory.created.size());
    }

    @Test
    public void identityChangeReplacesControlPlaneModel() {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get("z-fold-7", "http://server");

        BeaconViewModel second = session.get("z-fold-8", "http://server");

        assertNotSame(first, second);
        assertEquals(2, factory.created.size());
    }

    @Test
    public void closeForcesNextIdentityLookupToCreate() {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get("z-fold-7", "http://server");

        session.close();
        BeaconViewModel second = session.get("z-fold-7", "http://server");

        assertNotSame(first, second);
        assertEquals(2, factory.created.size());
    }

    private static final class RecordingModelFactory implements BeaconViewModelSession.Factory {
        private final List<BeaconViewModel> created = new ArrayList<>();

        @Override
        public BeaconViewModel create(String clientId, String serverUrl) {
            BeaconViewModel model = new BeaconViewModel(clientId, serverUrl, new NoOpService());
            created.add(model);
            return model;
        }
    }

    private static final class NoOpService implements BeaconViewModel.BeaconService {
        private final BeaconApiClient.BeaconResult result = new BeaconApiClient.BeaconResult(200, "{}");

        @Override
        public BeaconApiClient.BeaconResult hello() { return result; }

        @Override
        public BeaconApiClient.BeaconResult patchProfile(BeaconApiClient.ProfilePatch patch) { return result; }

        @Override
        public BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) { return result; }

        @Override
        public BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) { return result; }

        @Override
        public BeaconApiClient.BeaconResult beacon(boolean active) { return result; }

        @Override
        public BeaconApiClient.BeaconResult games() { return result; }

        @Override
        public BeaconApiClient.BeaconResult requestPlan(BeaconApiClient.GameSelection game) { return result; }

        @Override
        public BeaconApiClient.BeaconResult launch(BeaconApiClient.GameSelection game) { return result; }

        @Override
        public BeaconApiClient.BeaconResult stopStream() { return result; }

        @Override
        public BeaconApiClient.BeaconResult disconnect() { return result; }

        @Override
        public BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) { return result; }

        @Override
        public BeaconApiClient.BeaconResult emergencyRestore() { return result; }
    }
}
