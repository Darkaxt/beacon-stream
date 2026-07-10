package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
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
    public void loadGamesFormatsServerCatalog() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"games\":[{\"id\":\"steam-shortcut:3767414131\",\"title\":\"Dispatch\",\"source\":\"steam-shortcut\",\"installed\":true}],\"diagnostics\":[]}");
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.loadGames();

        assertEquals("games", service.lastAction);
        assertEquals("games: 200", model.status());
        assertTrue(model.latestGames().contains("Dispatch"));
        assertTrue(model.latestGames().contains("steam-shortcut:3767414131"));
    }

    @Test
    public void loadGamesStoresParsedCatalogEntries() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"games\":[{\"id\":\"steam-shortcut:3767414131\",\"title\":\"Dispatch\",\"source\":\"steam-shortcut\",\"installed\":true}]}");
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.loadGames();

        assertEquals(1, model.latestGameEntries().size());
        assertEquals("steam-shortcut:3767414131", model.latestGameEntries().get(0).id());
        assertEquals("Dispatch [steam-shortcut] steam-shortcut:3767414131 installed", model.latestGameEntries().get(0).displayLabel());
    }

    @Test
    public void beaconRecordsClientActivity() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.beacon(true);

        assertEquals("beacon", service.lastAction);
        assertTrue(service.lastBeaconActive);
        assertEquals("beacon: 200", model.status());
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
    public void launchDelegatesServerConnectionLaunchUri() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"launchUri\":\"moonlight://stream/z-fold-7\"}}}");
        RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, launcher);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("moonlight://stream/z-fold-7", launcher.launchedUri);
    }

    @Test
    public void launchStartsNativeSessionBeforeDelegatingFallbackLaunchUri() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, validNativeSessionResponse());
        RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
        RecordingNativeStreamClient nativeStreamClient = new RecordingNativeStreamClient(
            NativeStreamStartResult.started(
                "Native Moonlight stream started.",
                NativeStreamPresentation.encodedVideo(
                    "rtsp://10.0.2.2:48010/session/123",
                    "moonlight-native",
                    2560,
                    1600,
                    120)));
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7",
            "http://server",
            service,
            launcher,
            nativeStreamClient);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("", launcher.launchedUri);
        assertTrue(nativeStreamClient.startedConnection.nativeSessionValid());
        assertEquals("encoded-video", model.latestNativeStreamPresentation().kind());
    }

    @Test
    public void launchDoesNotDelegateConnectionUriWhenServerLaunchFails() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            503,
            "{\"stream\":{\"connection\":{\"launchUri\":\"moonlight://stream/z-fold-7\"}}}");
        RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, launcher);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("", launcher.launchedUri);
    }

    @Test
    public void launchDoesNotDelegateConnectionUriWhenServerDoesNotProvideOne() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, "{\"state\":\"streaming\",\"stream\":{\"fps\":120}}");
        RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, launcher);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("", launcher.launchedUri);
    }

    @Test
    public void launchReportsEndpointOnlyConnectionWhenLaunchUriIsMissing() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}]}}}");
        RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, launcher);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("", launcher.launchedUri);
        assertEquals(
            "GameStream endpoint map is incomplete. Missing required endpoints: video, control, audio. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010",
            model.latestError());
    }

    @Test
    public void launchStartsNativeStreamClientForSupportedEndpointOnlyConnection() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"video\",\"uri\":\"beacon-test://pattern/color-bars\"}]}}}");
        RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
        RecordingNativeStreamClient nativeStreamClient = new RecordingNativeStreamClient(
            NativeStreamStartResult.started(
                "Native stream ready. protocol=beacon-test endpoints=video=beacon-test://pattern/color-bars",
                NativeStreamPresentation.colorBars("beacon-test://pattern/color-bars")));
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, launcher, nativeStreamClient);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("", launcher.launchedUri);
        assertEquals("beacon-test", nativeStreamClient.startedConnection.protocol());
        assertEquals(
            "Native stream ready. protocol=beacon-test endpoints=video=beacon-test://pattern/color-bars",
            model.latestNativeStream());
        assertTrue(model.latestNativeStreamPresentation().active());
        assertEquals("color-bars", model.latestNativeStreamPresentation().kind());
        assertEquals("", model.latestError());
    }

    @Test
    public void stopStreamStopsNativeStreamStateAfterSuccessfulServerStop() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"video\",\"uri\":\"beacon-test://pattern/color-bars\"}]}}}");
        RecordingNativeStreamClient nativeStreamClient = new RecordingNativeStreamClient(
            NativeStreamStartResult.started(
                "Native stream ready. protocol=beacon-test endpoints=video=beacon-test://pattern/color-bars",
                NativeStreamPresentation.colorBars("beacon-test://pattern/color-bars")));
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7",
            "http://server",
            service,
            new RecordingStreamConnectionLauncher(),
            nativeStreamClient);
        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));
        service.next = new BeaconApiClient.BeaconResult(200, "{\"state\":\"stopped\"}");

        model.stopStream();

        assertEquals(1, nativeStreamClient.stopCount);
        assertEquals("", model.latestNativeStream());
        assertFalse(model.latestNativeStreamPresentation().active());
    }

    @Test
    public void launchUriLaunchClearsPreviousNativeStreamState() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"video\",\"uri\":\"beacon-test://pattern/color-bars\"}]}}}");
        RecordingStreamConnectionLauncher launcher = new RecordingStreamConnectionLauncher();
        RecordingNativeStreamClient nativeStreamClient = new RecordingNativeStreamClient(
            NativeStreamStartResult.started(
                "Native stream ready. protocol=beacon-test endpoints=video=beacon-test://pattern/color-bars",
                NativeStreamPresentation.colorBars("beacon-test://pattern/color-bars")));
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, launcher, nativeStreamClient);
        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));
        service.next = new BeaconApiClient.BeaconResult(
            200,
            "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"launchUri\":\"moonlight://stream/z-fold-7\"}}}");

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        assertEquals("moonlight://stream/z-fold-7", launcher.launchedUri);
        assertEquals(1, nativeStreamClient.stopCount);
        assertEquals("", model.latestNativeStream());
        assertFalse(model.latestNativeStreamPresentation().active());
    }

    @Test
    public void sendInputCallsOwningClientInputRoute() throws Exception {
        FakeService service = new FakeService();
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service);

        model.sendInput(BeaconApiClient.InputBatch.pointerTap(4, 0.5, 0.5));

        assertEquals("input", service.lastAction);
        assertEquals("input: 200", model.status());
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

    private static String validNativeSessionResponse() {
        return "{" +
            "\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"launchUri\":\"moonlight://fallback\"}}," +
            "\"nativeSession\":{" +
            "\"address\":\"10.0.2.2\"," +
            "\"serverAppVersion\":\"7.1.431.0\"," +
            "\"serverGfeVersion\":\"3.27.0.120\"," +
            "\"rtspSessionUrl\":\"rtsp://10.0.2.2:48010/session/123\"," +
            "\"serverCodecModeSupport\":769," +
            "\"width\":2560," +
            "\"height\":1600," +
            "\"fps\":120," +
            "\"bitrateKbps\":45000," +
            "\"packetSize\":1024," +
            "\"streamingMode\":\"local\"," +
            "\"audioConfiguration\":\"stereo\"," +
            "\"videoFormat\":\"hevc-main10\"," +
            "\"clientRefreshRateX100\":12000," +
            "\"colorSpace\":\"rec2020\"," +
            "\"colorRange\":\"full\"," +
            "\"encryptionMode\":\"all\"," +
            "\"remoteInputAesKey\":\"AAECAwQFBgcICQoLDA0ODw==\"," +
            "\"remoteInputAesIv\":\"EBESExQVFhcYGRobHB0eHw==\"}}";
    }

    private static final class RecordingStreamConnectionLauncher implements StreamConnectionLauncher {
        String launchedUri = "";

        @Override
        public void launch(String launchUri) {
            launchedUri = launchUri;
        }
    }

    private static final class RecordingNativeStreamClient implements NativeStreamClient {
        private final NativeStreamStartResult result;
        StreamConnectionDescriptor startedConnection;
        int stopCount;

        RecordingNativeStreamClient(NativeStreamStartResult result) {
            this.result = result;
        }

        @Override
        public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
            startedConnection = connection;
            return result;
        }

        @Override
        public void stop() {
            stopCount++;
        }
    }

    private static final class FakeService implements BeaconViewModel.BeaconService {
        String lastAction = "";
        private final StringBuilder actions = new StringBuilder();
        BeaconApiClient.ProfilePatch lastPatch;
        boolean lastBeaconActive;
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
        public BeaconApiClient.BeaconResult games() throws IOException {
            record("games");
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult beacon(boolean active) throws IOException {
            record("beacon");
            lastBeaconActive = active;
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
        public BeaconApiClient.BeaconResult sendInput(BeaconApiClient.InputBatch input) throws IOException {
            record("input");
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
