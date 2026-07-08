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

    @Test
    public void preflightAndPlanPatchesProfileThenReportsFactsThenRequestsPlan() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.preflightAndPlan(
            new BeaconApiClient.ProfilePatch(),
            defaultCapabilities(),
            defaultTelemetry(),
            BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("patch,capabilities,telemetry,plan", service.actions());
        assertEquals("plan: 200", model.status());
    }

    @Test
    public void preflightAndLaunchPatchesProfileThenReportsFactsThenLaunches() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.preflightAndLaunch(
            new BeaconApiClient.ProfilePatch(),
            defaultCapabilities(),
            defaultTelemetry(),
            BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("patch,capabilities,telemetry,launch", service.actions());
        assertEquals("launch: 200", model.status());
    }

    private static BeaconApiClient.ClientCapabilities defaultCapabilities() {
        return new BeaconApiClient.ClientCapabilities(true, true, true, false, false, 120, true, "2560x1600@120");
    }

    private static BeaconApiClient.ClientTelemetry defaultTelemetry() {
        return new BeaconApiClient.ClientTelemetry(8, 0.0, 20, 120, "wifi-7", 80, "nominal");
    }

    private static final class FakeService implements BeaconViewModel.BeaconService {
        String lastAction = "";
        private final StringBuilder actions = new StringBuilder();
        BeaconApiClient.ProfilePatch lastPatch;
        BeaconApiClient.BeaconResult next = new BeaconApiClient.BeaconResult(200, "{}");

        String actions() {
            return actions.toString();
        }

        private void record(String action) {
            lastAction = action;
            if (actions.length() > 0) {
                actions.append(',');
            }
            actions.append(action);
        }

        @Override
        public BeaconApiClient.BeaconResult hello() throws IOException {
            record("hello");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException {
            record("patch");
            lastPatch = patch;
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException {
            record("capabilities");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException {
            record("telemetry");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult requestPlan(BeaconApiClient.GameSelection game) throws IOException {
            record("plan");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult launch(BeaconApiClient.GameSelection game) throws IOException {
            record("launch");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult stopStream() throws IOException {
            record("stop");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult disconnect() throws IOException {
            record("disconnect");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) throws IOException {
            record("quit");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult emergencyRestore() throws IOException {
            record("restore");
            return next;
        }
    }
}
