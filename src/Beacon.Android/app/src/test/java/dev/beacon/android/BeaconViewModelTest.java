package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class BeaconViewModelTest {
    @Test
    public void initialStateShowsConfiguredClientAndServer() {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        assertEquals("z-fold-7", model.clientId());
        assertEquals("http://server", model.serverUrl());
        assertEquals("Idle", model.status());
    }

    @Test
    public void refreshCallsHelloFlow() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.refresh();

        assertEquals("hello", service.lastAction);
        assertEquals("hello: 200", model.status());
    }

    @Test
    public void patchActionUsesAllowedPatchObject() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);
        BeaconApiClient.ProfilePatch patch = new BeaconApiClient.ProfilePatch();
        patch.preferredWidth = 2560;
        patch.preferredHeight = 1600;
        patch.preferredRefreshHz = 120;

        model.patchProfile(patch);

        assertEquals("patch", service.lastAction);
        assertEquals(2560, service.lastPatch.preferredWidth.intValue());
        assertEquals(1600, service.lastPatch.preferredHeight.intValue());
    }

    @Test
    public void emergencyRestoreCallsOwningClientAction() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.emergencyRestore();

        assertEquals("restore", service.lastAction);
        assertEquals("emergency restore: 200", model.status());
    }

    @Test
    public void launchRecordsServerSelectedStreamState() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, "{\"state\":\"streaming\",\"stream\":{\"fps\":120}}");
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("launch", service.lastAction);
        assertTrue(model.latestStream().contains("\"state\":\"streaming\""));
    }

    private static final class FakeService implements BeaconViewModel.BeaconService {
        String lastAction = "";
        BeaconApiClient.ProfilePatch lastPatch;
        BeaconApiClient.BeaconResult next = new BeaconApiClient.BeaconResult(200, "{}");

        @Override
        public BeaconApiClient.BeaconResult hello() throws IOException {
            lastAction = "hello";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException {
            lastAction = "patch";
            lastPatch = patch;
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException {
            lastAction = "capabilities";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException {
            lastAction = "telemetry";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult requestPlan(BeaconApiClient.GameSelection game) throws IOException {
            lastAction = "plan";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult launch(BeaconApiClient.GameSelection game) throws IOException {
            lastAction = "launch";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult stopStream() throws IOException {
            lastAction = "stop";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult disconnect() throws IOException {
            lastAction = "disconnect";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) throws IOException {
            lastAction = "quit";
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult emergencyRestore() throws IOException {
            lastAction = "restore";
            return next;
        }
    }
}
