package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.Executors;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicInteger;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;
import static org.junit.Assert.assertThrows;

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
    public void launchHandsGrantToStreamCoreAndInputUsesNativeRoute() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, grantBody());
        RecordingCoreBindings bindings = new RecordingCoreBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "https://server", service, core);
        BeaconApiClient.GameSelection game = BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131");

        model.launch(game);
        model.sendInput(BeaconApiClient.InputBatch.pointerTap(4, 0.5, 0.5));
        model.close();

        assertEquals("launch", service.actions());
        assertEquals(game.gameId, service.lastGame.gameId);
        assertEquals(grantBody(), model.latestStream());
        assertEquals(1, bindings.startCount);
        assertEquals(1, bindings.inputCount);
        assertEquals(1, bindings.releaseCount);
    }

    @Test
    public void reconnectHandsFreshSameSessionTicketToTheOwnedCore() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, grantBody("AQID"));
        RecordingCoreBindings bindings = new RecordingCoreBindings();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings, frame -> { }, Executors.newSingleThreadExecutor());
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "https://server", service, core);

        model.launch(BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131"));
        service.next = new BeaconApiClient.BeaconResult(200, grantBody("BAUG"));
        model.reconnect();

        assertEquals(2, bindings.startCount);
        assertEquals(1, bindings.stopCount);
        assertEquals(Arrays.asList(
            Arrays.asList((byte) 1, (byte) 2, (byte) 3),
            Arrays.asList((byte) 4, (byte) 5, (byte) 6)), bindings.ticketSnapshots);
        assertTrue(allZero(bindings.ticketReferences.get(0)));
        assertTrue(allZero(bindings.ticketReferences.get(1)));
        model.close();
    }

    @Test
    public void preflightAndLaunchKeepsServerOwnedOrdering() throws Exception {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, grantBody());
        BeaconStreamCore core = new BeaconStreamCore(
            new RecordingCoreBindings(), frame -> { }, Executors.newSingleThreadExecutor());
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "https://server", service, core);

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
        BeaconStreamCore core = new BeaconStreamCore(
            new RecordingCoreBindings(), frame -> { }, Executors.newSingleThreadExecutor());
        BeaconViewModel model = new BeaconViewModel("z-fold-7", "http://server", service, core);

        model.beacon(true);
        model.sendInput(BeaconApiClient.InputBatch.pointerTap(4, 0.5, 0.5));
        model.stopStream();
        model.disconnect();
        model.quit(new BeaconApiClient.QuitState(false));
        model.emergencyRestore();

        assertEquals("beacon,stop,disconnect,quit,restore", service.actions());
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

    @Test
    public void terminalCloseRejectsEveryStreamCorePathWithoutAllocating() {
        FakeService service = new FakeService();
        service.next = new BeaconApiClient.BeaconResult(200, grantBody());
        AtomicInteger allocations = new AtomicInteger();
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service,
            (sink, failureObserver, benchmarkObserver) -> {
                allocations.incrementAndGet();
                throw new AssertionError("StreamCore allocated after close.");
            });

        model.close();
        model.close();

        assertThrows(IllegalStateException.class, () -> model.launch(
            BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131")));
        assertThrows(IllegalStateException.class, () -> model.sendInput(
            BeaconApiClient.InputBatch.pointerTap(1, 0.5, 0.5)));
        assertThrows(IllegalStateException.class, model::reconnect);
        assertThrows(IllegalStateException.class, model::ownedStreamCore);
        assertEquals(0, allocations.get());
        assertEquals("", service.actions());
    }

    @Test
    public void benchmarkPrepareStartsNativeRunAndCompletionSubmitsRawEvidence() throws Exception {
        FakeService service = new FakeService();
        service.benchmarkPrepare = new BeaconApiClient.BeaconResult(200, benchmarkGrantBody());
        RecordingBenchmarkCoreFactory factory = new RecordingBenchmarkCoreFactory();
        RecordingDeviceBenchmarkRunner deviceRunner = new RecordingDeviceBenchmarkRunner();
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service, factory);

        model.runBenchmark(benchmarkRequest("manual"), deviceRunner);
        assertEquals(0, deviceRunner.startCount);
        factory.emitCompletedNetworkResult();
        deviceRunner.awaitStarted();
        assertEquals(1, factory.bindings.stopCount);
        assertEquals(1, deviceRunner.startCount);
        assertEquals("benchmark prepare", service.actions());
        deviceRunner.emitCompleted();
        service.awaitBenchmarkCompletion();

        assertEquals("benchmark prepare,benchmark complete", service.actions());
        assertEquals(1, factory.bindings.startCount);
        assertEquals(1, factory.bindings.stopCount);
        assertEquals("benchmark complete: 200", model.status());
        assertEquals("beacon-h264-high-8-1280x720-60-v1", deviceRunner.plan.decoderRounds().get(0).vectorId());
        assertTrue(service.lastBenchmarkCompletion.toJson().toString().contains("\"rttMs\":2.5"));
        model.close();
    }

    @Test
    public void reusedBenchmarkEvidenceDoesNotAllocateStreamCore() throws Exception {
        FakeService service = new FakeService();
        service.benchmarkPrepare = new BeaconApiClient.BeaconResult(
            200,
            "{\"disposition\":\"reuse\",\"runId\":\"3c13df40-26c4-40c6-8414-268734f1024d\",\"connection\":null}");
        AtomicInteger allocations = new AtomicInteger();
        RecordingDeviceBenchmarkRunner deviceRunner = new RecordingDeviceBenchmarkRunner();
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service,
            (sink, failureObserver, benchmarkObserver) -> {
                allocations.incrementAndGet();
                throw new AssertionError("Reused evidence allocated StreamCore.");
            });

        model.runBenchmark(benchmarkRequest("automatic"), deviceRunner);

        assertEquals("benchmark prepare", service.actions());
        assertEquals(0, allocations.get());
        assertEquals(0, deviceRunner.startCount);
        assertEquals("benchmark reuse: 200", model.status());
        model.close();
    }

    @Test
    public void nativeStartFailureCancelsPreparedBenchmarkRun() throws Exception {
        FakeService service = new FakeService();
        service.benchmarkPrepare = new BeaconApiClient.BeaconResult(200, benchmarkGrantBody());
        RecordingDeviceBenchmarkRunner deviceRunner = new RecordingDeviceBenchmarkRunner();
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service,
            (sink, failureObserver, benchmarkObserver) -> new BeaconStreamCore(
                new RejectingCoreBindings(),
                sink,
                Executors.newSingleThreadExecutor(),
                () -> { },
                failureObserver,
                benchmarkObserver));

        assertThrows(IllegalStateException.class, () ->
            model.runBenchmark(benchmarkRequest("manual"), deviceRunner));

        assertEquals("benchmark prepare,benchmark cancel", service.actions());
        model.close();
    }

    @Test
    public void explicitBenchmarkCancellationStopsNativeRunAndCancelsServerRuntime() throws Exception {
        FakeService service = new FakeService();
        service.benchmarkPrepare = new BeaconApiClient.BeaconResult(200, benchmarkGrantBody());
        RecordingBenchmarkCoreFactory factory = new RecordingBenchmarkCoreFactory();
        RecordingDeviceBenchmarkRunner deviceRunner = new RecordingDeviceBenchmarkRunner();
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service, factory);

        model.runBenchmark(benchmarkRequest("manual"), deviceRunner);
        factory.emitCompletedNetworkResult();
        deviceRunner.awaitStarted();
        model.cancelBenchmark();
        deviceRunner.awaitCancelled();

        assertEquals("benchmark prepare,benchmark cancel", service.actions());
        assertEquals(1, factory.bindings.stopCount);
        assertEquals(1, deviceRunner.cancelCount);
        assertEquals("benchmark cancel: 200", model.status());
        model.close();
    }

    @Test
    public void nativeTransportLossCancelsServerBenchmarkWithoutCrashingCallback() throws Exception {
        FakeService service = new FakeService();
        service.benchmarkPrepare = new BeaconApiClient.BeaconResult(200, benchmarkGrantBody());
        RecordingBenchmarkCoreFactory factory = new RecordingBenchmarkCoreFactory();
        RecordingDeviceBenchmarkRunner deviceRunner = new RecordingDeviceBenchmarkRunner();
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service, factory);

        model.runBenchmark(benchmarkRequest("automatic"), deviceRunner);
        factory.emitConnectionLost();
        service.awaitBenchmarkCancellation();

        assertEquals("benchmark prepare,benchmark cancel", service.actions());
        assertEquals(0, deviceRunner.cancelCount);
        assertEquals("benchmark transport: failed", model.status());
        model.close();
    }

    @Test
    public void rejectedCompletionEvidenceCancelsStillPendingServerRuntime() throws Exception {
        FakeService service = new FakeService();
        service.benchmarkPrepare = new BeaconApiClient.BeaconResult(200, benchmarkGrantBody());
        service.next = new BeaconApiClient.BeaconResult(400, "invalid benchmark evidence");
        RecordingBenchmarkCoreFactory factory = new RecordingBenchmarkCoreFactory();
        RecordingDeviceBenchmarkRunner deviceRunner = new RecordingDeviceBenchmarkRunner();
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service, factory);

        model.runBenchmark(benchmarkRequest("manual"), deviceRunner);
        factory.emitCompletedNetworkResult();
        deviceRunner.awaitStarted();
        deviceRunner.emitCompleted();
        service.awaitBenchmarkCancellation();

        assertEquals(
            "benchmark prepare,benchmark complete,benchmark cancel",
            service.actions());
        assertEquals("benchmark complete: 400", model.status());
        assertEquals("invalid benchmark evidence", model.latestError());
        model.close();
    }

    @Test
    public void malformedPreparedGrantCancelsRunIdBeforeReportingContractFailure() throws Exception {
        FakeService service = new FakeService();
        service.benchmarkPrepare = new BeaconApiClient.BeaconResult(
            200,
            "{\"disposition\":\"start-new\",\"runId\":\"3c13df40-26c4-40c6-8414-268734f1024d\",\"connection\":null}");
        BeaconViewModel model = new BeaconViewModel(
            "z-fold-7", "https://server", service);
        RecordingDeviceBenchmarkRunner deviceRunner = new RecordingDeviceBenchmarkRunner();

        assertThrows(IllegalArgumentException.class, () ->
            model.runBenchmark(benchmarkRequest("manual"), deviceRunner));

        assertEquals("benchmark prepare,benchmark cancel", service.actions());
        assertEquals(0, deviceRunner.startCount);
        model.close();
    }

    private static BeaconApiClient.ClientCapabilities capabilities() {
        return new BeaconApiClient.ClientCapabilities(true, true, true, false, false, 120, true, "2560x1600@120");
    }

    private static BeaconApiClient.ClientTelemetry telemetry() {
        return new BeaconApiClient.ClientTelemetry(8, 0.0, 20, 120, "wifi-7", 80, "nominal");
    }

    private static String grantBody() {
        return grantBody("AQID");
    }

    private static String grantBody(String ticket) {
        return "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"" + ticket + "\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":1,\"planExplanation\":\"selected\",\"sessionId\":\"s\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1280,\"height\":720,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"}}}";
    }

    private static BeaconBenchmarkPrepareRequest benchmarkRequest(String trigger) {
        BeaconNetworkIdentityHasher hasher = new BeaconNetworkIdentityHasher(
            new BeaconNetworkIdentityHasher.SaltStorage() {
                private String value = "";
                @Override public String read() { return value; }
                @Override public boolean write(String encodedSalt) {
                    value = encodedSalt;
                    return true;
                }
            },
            () -> new byte[32]);
        return new BeaconBenchmarkPrepareRequest(
            trigger,
            new BeaconBenchmarkPrepareRequest.FingerprintSet(
                BeaconBenchmarkPrepareRequest.NetworkFingerprint.fromLocalNetwork(
                    3,
                    "server-route",
                    "wifi",
                    "192.168.1.0/24",
                    "6ghz",
                    5,
                    "1000+mbps",
                    "private-ssid",
                    "00:11:22:33:44:55",
                    hasher),
                new BeaconBenchmarkPrepareRequest.HardwareFingerprint(
                    3,
                    "device-revision",
                    "15",
                    "1.0",
                    "display-revision",
                    "codec-revision")));
    }

    private static List<BeaconBenchmarkCompletionRequest.DecoderSample> decoderSamples() {
        return Arrays.asList(new BeaconBenchmarkCompletionRequest.DecoderSample(
            "h264", "high", 8, 2560, 1600, 120, true,
            120.0, 5.0, 9.0, 0, 0, false, false));
    }

    private static List<BeaconBenchmarkCompletionRequest.PowerSample> powerSamples() {
        return Arrays.asList(new BeaconBenchmarkCompletionRequest.PowerSample(
            80, false, "nominal"));
    }

    private static String benchmarkGrantBody() {
        return "{\"disposition\":\"start-new\",\"runId\":\"3c13df40-26c4-40c6-8414-268734f1024d\"," +
            "\"hardwarePlan\":{\"schemaVersion\":1,\"samplePowerBeforeAndAfterEachRound\":true,\"decoderRounds\":[{" +
            "\"vectorId\":\"beacon-h264-high-8-1280x720-60-v1\",\"codec\":\"h264\",\"profile\":\"high\"," +
            "\"bitDepth\":8,\"width\":1280,\"height\":720,\"targetFps\":60,\"repetitionCount\":3}]}," +
            "\"connection\":{" +
            "\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\"," +
            "\"planRevision\":9,\"planExplanation\":\"benchmark\"," +
            "\"sessionId\":\"benchmark:3c13df40-26c4-40c6-8414-268734f1024d\"," +
            "\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\"," +
            "\"benchmark\":{\"runId\":\"3c13df40-26c4-40c6-8414-268734f1024d\",\"schemaVersion\":1," +
            "\"reliableRound\":{\"packetCount\":16,\"payloadBytes\":32768,\"measurementIntervalUs\":250000}," +
            "\"datagramRound\":{\"packetCount\":64,\"payloadBytes\":1000,\"measurementIntervalUs\":250000}," +
            "\"runToken\":\"AAECAwQFBgcICQoLDA0ODw==\"}}}";
    }

    private static boolean allZero(byte[] bytes) {
        for (byte value : bytes) {
            if (value != 0) return false;
        }
        return true;
    }

    private static final class RecordingCoreBindings implements BeaconStreamCore.Bindings {
        int startCount;
        int inputCount;
        int releaseCount;
        int stopCount;
        final List<byte[]> ticketReferences = new ArrayList<>();
        final List<List<Byte>> ticketSnapshots = new ArrayList<>();

        @Override public long create(BeaconStreamCore.NativeCallbacks callbacks) { return 9; }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) {
            startCount++;
            ticketReferences.add(grant.ticket);
            List<Byte> snapshot = new ArrayList<>();
            for (byte value : grant.ticket) snapshot.add(value);
            ticketSnapshots.add(snapshot);
            return true;
        }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) { inputCount++; }
        @Override public void replaceSurface(long handle, Object surface) { }
        @Override public void stop(long handle) { stopCount++; }
        @Override public void release(long handle) { releaseCount++; }
    }

    private static final class RecordingBenchmarkCoreFactory implements BeaconViewModel.StreamCoreFactory {
        private final RecordingBenchmarkCoreBindings bindings = new RecordingBenchmarkCoreBindings();

        @Override
        public BeaconStreamCore create(
            BeaconStreamCore.EncodedFrameSink sink,
            BeaconStreamCore.FailureObserver failureObserver,
            BeaconStreamCore.BenchmarkObserver benchmarkObserver) {
            return new BeaconStreamCore(
                bindings,
                sink,
                Executors.newSingleThreadExecutor(),
                () -> { },
                failureObserver,
                benchmarkObserver);
        }

        void emitCompletedNetworkResult() {
            bindings.callbacks.onBenchmarkCompleted(
                96.5,
                new long[] { 0, 1 },
                new int[] { 1000, 1000 },
                new long[] { 2000, 2500 },
                new long[] { 0, 300 },
                new int[] { 0, 1 },
                new boolean[] { true, false },
                bindings.generation);
        }

        void emitConnectionLost() {
            bindings.callbacks.onConnectionLost(bindings.generation);
        }
    }

    private static final class RecordingDeviceBenchmarkRunner implements BeaconDeviceBenchmarkRunner {
        private BeaconBenchmarkHardwarePlan plan;
        private Observer observer;
        private int startCount;
        private int cancelCount;
        private final CountDownLatch started = new CountDownLatch(1);
        private final CountDownLatch cancelled = new CountDownLatch(1);

        @Override
        public Run start(BeaconBenchmarkHardwarePlan plan, Observer observer) {
            this.plan = plan;
            this.observer = observer;
            startCount++;
            started.countDown();
            return () -> {
                cancelCount++;
                cancelled.countDown();
            };
        }

        void awaitStarted() throws InterruptedException {
            started.await();
        }

        void awaitCancelled() throws InterruptedException {
            cancelled.await();
        }

        void emitCompleted() {
            observer.onCompleted(new BeaconBenchmarkDeviceEvidence(decoderSamples(), powerSamples()));
        }
    }

    private static final class RecordingBenchmarkCoreBindings implements BeaconStreamCore.Bindings {
        private BeaconStreamCore.NativeCallbacks callbacks;
        private long generation;
        private int startCount;
        private int stopCount;

        @Override public long create(BeaconStreamCore.NativeCallbacks callbacks) {
            this.callbacks = callbacks;
            return 10;
        }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) {
            startCount++;
            generation = grant.generation;
            return true;
        }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) { }
        @Override public void replaceSurface(long handle, Object surface) { }
        @Override public void stop(long handle) { stopCount++; }
        @Override public void release(long handle) { }
    }

    private static final class RejectingCoreBindings implements BeaconStreamCore.Bindings {
        @Override public long create(BeaconStreamCore.NativeCallbacks callbacks) { return 11; }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) { return false; }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) { }
        @Override public void replaceSurface(long handle, Object surface) { }
        @Override public void stop(long handle) { }
        @Override public void release(long handle) { }
    }

    private static final class FakeService implements BeaconViewModel.BeaconService {
        private final StringBuilder actionLog = new StringBuilder();
        private BeaconApiClient.BeaconResult next = new BeaconApiClient.BeaconResult(200, "ok body");
        private BeaconApiClient.GameSelection lastGame;
        private BeaconApiClient.BeaconResult benchmarkPrepare = new BeaconApiClient.BeaconResult(200, "{}");
        private BeaconBenchmarkCompletionRequest lastBenchmarkCompletion;
        private final CountDownLatch benchmarkCompleted = new CountDownLatch(1);
        private final CountDownLatch benchmarkCancelled = new CountDownLatch(1);

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

        void awaitBenchmarkCompletion() throws InterruptedException {
            benchmarkCompleted.await();
        }

        void awaitBenchmarkCancellation() throws InterruptedException {
            benchmarkCancelled.await();
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
        public BeaconApiClient.BeaconResult prepareBenchmark(
            BeaconBenchmarkPrepareRequest request) throws IOException {
            record("benchmark prepare");
            return benchmarkPrepare;
        }

        @Override
        public BeaconApiClient.BeaconResult completeBenchmark(
            String runId,
            BeaconBenchmarkCompletionRequest request) throws IOException {
            lastBenchmarkCompletion = request;
            BeaconApiClient.BeaconResult result = record("benchmark complete");
            benchmarkCompleted.countDown();
            return result;
        }

        @Override
        public BeaconApiClient.BeaconResult cancelBenchmark(String runId) throws IOException {
            BeaconApiClient.BeaconResult result = record("benchmark cancel");
            benchmarkCancelled.countDown();
            return result;
        }

        @Override
        public BeaconApiClient.BeaconResult reconnect() throws IOException {
            return record("reconnect");
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
