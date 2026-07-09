package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;
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
    public void identityChangeStopsPreviousNativeStream() throws Exception {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get("z-fold-7", "http://server");
        CreatedModel firstCreated = factory.created.get(0);
        firstCreated.service.next = gameStreamResponse();
        first.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        BeaconViewModel second = session.get("z-fold-8", "http://server");

        assertNotSame(first, second);
        assertEquals(1, firstCreated.nativeStreamClient.stopCount);
        assertEquals(2, factory.created.size());
    }

    @Test
    public void closeStopsActiveNativeStreamAndForcesNextCreate() throws Exception {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get("z-fold-7", "http://server");
        CreatedModel firstCreated = factory.created.get(0);
        firstCreated.service.next = gameStreamResponse();
        first.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));

        session.close();
        BeaconViewModel second = session.get("z-fold-7", "http://server");

        assertNotSame(first, second);
        assertEquals(1, firstCreated.nativeStreamClient.stopCount);
        assertEquals(2, factory.created.size());
    }

    private static BeaconApiClient.BeaconResult gameStreamResponse() {
        return new BeaconApiClient.BeaconResult(
            200,
            "{\"state\":\"streaming\",\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");
    }

    private static final class RecordingModelFactory implements BeaconViewModelSession.Factory {
        private final List<CreatedModel> created = new ArrayList<>();

        @Override
        public BeaconViewModel create(String clientId, String serverUrl) {
            FakeService service = new FakeService();
            RecordingNativeStreamClient nativeStreamClient = new RecordingNativeStreamClient(
                NativeStreamStartResult.started("Native GameStream RTSP session started."));
            BeaconViewModel model = new BeaconViewModel(
                clientId,
                serverUrl,
                service,
                launchUri -> { },
                nativeStreamClient);
            created.add(new CreatedModel(model, service, nativeStreamClient));
            return model;
        }
    }

    private static final class CreatedModel {
        private final BeaconViewModel model;
        private final FakeService service;
        private final RecordingNativeStreamClient nativeStreamClient;

        private CreatedModel(
            BeaconViewModel model,
            FakeService service,
            RecordingNativeStreamClient nativeStreamClient) {
            this.model = model;
            this.service = service;
            this.nativeStreamClient = nativeStreamClient;
        }
    }

    private static final class RecordingNativeStreamClient implements NativeStreamClient {
        private final NativeStreamStartResult result;
        private int stopCount;

        private RecordingNativeStreamClient(NativeStreamStartResult result) {
            this.result = result;
        }

        @Override
        public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
            return result;
        }

        @Override
        public void stop() {
            stopCount++;
        }
    }

    private static final class FakeService implements BeaconViewModel.BeaconService {
        private BeaconApiClient.BeaconResult next = new BeaconApiClient.BeaconResult(200, "{}");

        @Override
        public BeaconApiClient.BeaconResult hello() throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult beacon(boolean active) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult games() throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult requestPlan(BeaconApiClient.GameSelection game) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult launch(BeaconApiClient.GameSelection game) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult sendInput(BeaconApiClient.InputBatch input) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult stopStream() throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult disconnect() throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) throws IOException {
            return next;
        }

        @Override
        public BeaconApiClient.BeaconResult emergencyRestore() throws IOException {
            return next;
        }
    }
}
