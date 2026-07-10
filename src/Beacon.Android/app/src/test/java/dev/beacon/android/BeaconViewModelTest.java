package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class BeaconViewModelTest {
    @Test
    public void exposesOnlyTheControlPlaneConstructor() {
        assertEquals(1, BeaconViewModel.class.getConstructors().length);
        assertEquals(3, BeaconViewModel.class.getConstructors()[0].getParameterCount());
    }

    @Test
    public void initialStateShowsConfiguredClientAndServer() {
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", new FakeService());

        assertEquals("z-fold-7", model.clientId());
        assertEquals("http://server", model.serverUrl());
        assertEquals("Idle", model.status());
    }

    @Test
    public void refreshCallsHelloFlow() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.refresh();

        assertEquals("hello", service.actions());
        assertEquals("hello: 200", model.status());
    }

    @Test
    public void loadGamesKeepsCatalogEntriesForSelection() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"games\":[{\"id\":\"steam-shortcut:3767414131\",\"title\":\"Dispatch\",\"source\":\"steam-shortcut\",\"installed\":true}]}");
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.loadGames();

        assertEquals(1, model.latestGameEntries().size());
        assertEquals("steam-shortcut:3767414131", model.latestGameEntries().get(0).id());
        assertTrue(model.latestGames().contains("Dispatch"));
    }

    @Test
    public void preflightAndPlanSendsClientFactsBeforeSelectedGame() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);
        BeaconApiClient.GameSelection game = BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131");

        model.preflightAndPlan(new BeaconApiClient.ProfilePatch(), capabilities(), telemetry(), game);

        assertEquals("patch,capabilities,telemetry,plan", service.actions());
        assertEquals(game.gameId, service.lastGame.gameId);
        assertEquals("plan body", model.latestPlan());
    }

    @Test
    public void launchRecordsServerResponseWithoutMediaHandoff() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, "launch body");
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);
        BeaconApiClient.GameSelection game = BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131");

        model.launch(game);

        assertEquals("launch", service.actions());
        assertEquals(game.gameId, service.lastGame.gameId);
        assertEquals("launch body", model.latestStream());
    }

    @Test
    public void preflightAndLaunchKeepsServerOwnedOrdering() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.preflightAndLaunch(
            new BeaconApiClient.ProfilePatch(),
            capabilities(),
            telemetry(),
            BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("patch,capabilities,telemetry,launch", service.actions());
    }

    @Test
    public void controlActionsRemainAvailableDuringMediaRecovery() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.beacon(true);
        model.sendInput(BeaconApiClient.InputBatch.pointerTap(4, 0.5, 0.5));
        model.stopStream();
        model.disconnect();
        model.quit(new BeaconApiClient.QuitState(false));
        model.emergencyRestore();

        assertEquals("beacon,input,stop,disconnect,quit,restore", service.actions());
        assertEquals("stop body", model.latestStream());
        assertEquals("emergency restore: 200", model.status());
    }

    @Test
    public void failedActionRecordsServerError() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(503, "stream unavailable");
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("stream unavailable", model.latestError());
        assertEquals("launch: 503", model.status());
    }

    private static BeaconApiClient.ClientCapabilities capabilities() {
        return new BeaconApiClient.ClientCapabilities(true, true, true, false, false, 120, true, "2560x1600@120");
    }

    private static BeaconApiClient.ClientTelemetry telemetry() {
        return new BeaconApiClient.ClientTelemetry(8, 0.0, 20, 120, "wifi-7", 80, "nominal");
    }

    private static final class FakeService implements BeaconViewModel.BeaconService {
        private final StringBuilder actionLog = new StringBuilder();
        private BeaconApiClient.BeaconResult next = new BeaconApiClient.BeaconResult(200, "ok body");
        private BeaconApiClient.GameSelection lastGame;

        String actions() {
            return actionLog.toString();
        }

        private BeaconApiClient.BeaconResult record(String action) {
            if (actionLog.length() > 0) {
                actionLog.append(',');
            }
            actionLog.append(action);
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult hello() throws IOException {
            return record("hello");
        }

        @Override
        public BeaconApiClient.BeaconResult patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException {
            return record("patch");
        }

        @Override
        public BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException {
            return record("capabilities");
        }

        @Override
        public BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException {
            return record("telemetry");
        }

        @Override
        public BeaconApiClient.BeaconResult beacon(boolean active) throws IOException {
            return record("beacon");
        }

        @Override
        public BeaconApiClient.BeaconResult games() throws IOException {
            return record("games");
        }

        @Override
        public BeaconApiClient.BeaconResult requestPlan(BeaconApiClient.GameSelection game) throws IOException {
            lastGame = game;
            BeaconApiClient.BeaconResult result = record("plan");
            return new BeaconApiClient.BeaconResult(result.statusCode(), "plan body");
        }

        @Override
        public BeaconApiClient.BeaconResult launch(BeaconApiClient.GameSelection game) throws IOException {
            lastGame = game;
            return record("launch");
        }

        @Override
        public BeaconApiClient.BeaconResult sendInput(BeaconApiClient.InputBatch input) throws IOException {
            return record("input");
        }

        @Override
        public BeaconApiClient.BeaconResult stopStream() throws IOException {
            record("stop");
            return new BeaconApiClient.BeaconResult(200, "stop body");
        }

        @Override
        public BeaconApiClient.BeaconResult disconnect() throws IOException {
            return record("disconnect");
        }

        @Override
        public BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) throws IOException {
            return record("quit");
        }

        @Override
        public BeaconApiClient.BeaconResult emergencyRestore() throws IOException {
            return record("restore");
        }
    }
}
