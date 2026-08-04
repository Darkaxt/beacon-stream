package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

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
    public void publicKeyFingerprintChangeReplacesControlPlaneModel() {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get(new BeaconClientConfig(
            "https://server",
            "z-fold-7",
            "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));

        BeaconViewModel second = session.get(new BeaconClientConfig(
            "https://server",
            "z-fold-7",
            "BBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB"));

        assertNotSame(first, second);
        assertEquals(2, factory.created.size());
    }

    @Test
    public void identityChangeDeactivatesAndQuitsActiveOldModelBeforeReplacement() throws Exception {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get("z-fold-7", "http://server");
        first.setForegroundDesired(true);
        first.onForeground(capabilities());

        session.get("z-fold-8", "http://server");

        assertEquals(
            "hello,capabilities,beacon active,beacon inactive,quit",
            factory.services.get(0).actions());
    }

    @Test
    public void serverChangeStillQuitsOldModelWhenInactiveCleanupFails() throws Exception {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get("z-fold-7", "http://old-server");
        first.setForegroundDesired(true);
        first.onForeground(capabilities());
        factory.services.get(0).failInactive = true;

        session.get("z-fold-7", "http://new-server");

        assertEquals(
            "hello,capabilities,beacon active,beacon inactive,quit",
            factory.services.get(0).actions());
    }

    @Test
    public void identityReplacementCleanupCreationAndActionRunOffCallingThread() throws Exception {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel first = session.get("z-fold-7", "http://old-server");
        first.setForegroundDesired(true);
        first.onForeground(capabilities());
        factory.threadEvents.clear();

        String callingThread = Thread.currentThread().getName();
        ExecutorService executor = Executors.newSingleThreadExecutor(
            runnable -> new Thread(runnable, "beacon-model-worker"));
        CompletableFuture<Void> finished = new CompletableFuture<>();
        try {
            session.execute(
                executor,
                new BeaconClientConfig("http://new-server", "z-fold-8"),
                model -> {
                    factory.threadEvents.add(
                        "action:" + (model == factory.created.get(1) ? "new" : "wrong") +
                            ":" + Thread.currentThread().getName());
                    finished.complete(null);
                });
            finished.get();
        } finally {
            executor.shutdown();
        }

        assertEquals(
            Arrays.asList(
                "beacon inactive:beacon-model-worker",
                "quit:beacon-model-worker",
                "create:z-fold-8:beacon-model-worker",
                "action:new:beacon-model-worker"),
            factory.threadEvents);
        for (String event : factory.threadEvents) {
            org.junit.Assert.assertFalse(event.endsWith(":" + callingThread));
        }
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

    @Test
    public void explicitSuccessfulQuitFollowedByCloseSendsOneServiceQuit() throws Exception {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel model = session.get("z-fold-7", "http://server");

        model.quit(new BeaconApiClient.QuitState(false));
        session.close();

        assertEquals("quit", factory.services.get(0).actions());
    }

    @Test
    public void failedExplicitQuitFollowedByCloseRetriesServiceQuit() throws Exception {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        BeaconViewModel model = session.get("z-fold-7", "http://server");
        factory.services.get(0).failNextQuit = true;

        model.quit(new BeaconApiClient.QuitState(false));
        session.close();

        assertEquals("quit,quit", factory.services.get(0).actions());
    }

    @Test
    public void closeWithoutExplicitQuitSendsOneServiceQuit() {
        RecordingModelFactory factory = new RecordingModelFactory();
        BeaconViewModelSession session = new BeaconViewModelSession(factory);
        session.get("z-fold-7", "http://server");

        session.close();

        assertEquals("quit", factory.services.get(0).actions());
    }

    private static final class RecordingModelFactory implements BeaconViewModelSession.Factory {
        private final List<BeaconViewModel> created = new ArrayList<>();
        private final List<RecordingService> services = new ArrayList<>();
        private final List<String> threadEvents = new ArrayList<>();

        @Override
        public BeaconViewModel create(BeaconClientConfig config) {
            threadEvents.add("create:" + config.clientId() + ":" + Thread.currentThread().getName());
            RecordingService service = new RecordingService(threadEvents);
            BeaconViewModel model = new BeaconViewModel(config.clientId(), config.serverUrl(), service);
            services.add(service);
            created.add(model);
            return model;
        }
    }

    private static BeaconApiClient.ClientCapabilities capabilities() {
        BeaconApiClient.ClientDisplayMode mode = new BeaconApiClient.ClientDisplayMode(2560, 1600, 120);
        return new BeaconApiClient.ClientCapabilities(
            true, true, true, false, false, 120, true, mode, Arrays.asList(mode));
    }

    private static final class RecordingService implements BeaconViewModel.BeaconService {
        private final BeaconApiClient.BeaconResult result = new BeaconApiClient.BeaconResult(200, "{}");
        private final StringBuilder actions = new StringBuilder();
        private final List<String> threadEvents;
        private boolean failInactive;
        private boolean failNextQuit;

        private RecordingService(List<String> threadEvents) {
            this.threadEvents = threadEvents;
        }

        String actions() { return actions.toString(); }

        private BeaconApiClient.BeaconResult record(String action) {
            if (actions.length() > 0) actions.append(',');
            actions.append(action);
            threadEvents.add(action + ":" + Thread.currentThread().getName());
            return result;
        }

        @Override
        public BeaconApiClient.BeaconResult hello() { return record("hello"); }

        @Override
        public BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) { return record("capabilities"); }

        @Override
        public BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) { return result; }

        @Override
        public BeaconApiClient.BeaconResult beacon(boolean active) throws java.io.IOException {
            BeaconApiClient.BeaconResult response = record(active ? "beacon active" : "beacon inactive");
            if (!active && failInactive) throw new java.io.IOException("inactive failed");
            return response;
        }

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
        public BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) {
            record("quit");
            if (failNextQuit) {
                failNextQuit = false;
                return new BeaconApiClient.BeaconResult(503, "quit failed");
            }
            return result;
        }

        @Override
        public BeaconApiClient.BeaconResult emergencyRestore() { return result; }
    }
}
