package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Intent;
import android.graphics.SurfaceTexture;
import android.os.Bundle;
import android.util.Log;
import android.view.Surface;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.runner.lifecycle.ActivityLifecycleCallback;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitor;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry;
import androidx.test.runner.lifecycle.Stage;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotEquals;

import static org.junit.Assert.assertTrue;
import static org.junit.Assert.assertThrows;
import static org.junit.Assume.assumeTrue;

@RunWith(AndroidJUnit4.class)
public final class BeaconStreamCoreInstrumentationTest {
    private static final String CredentialEvidenceFile = "beacon-gate3-client-credential";

    @Test
    public void testFrameCallbackUsesDedicatedThreadAndStopsAfterClose() throws InterruptedException {
        RecordingBindings bindings = new RecordingBindings();
        ExecutorService executor = Executors.newSingleThreadExecutor(r -> new Thread(r, "beacon-device-frame"));
        CountDownLatch delivered = new CountDownLatch(1);
        AtomicReference<String> callbackThread = new AtomicReference<>();
        AtomicInteger frames = new AtomicInteger();
        BeaconStreamCore core = new BeaconStreamCore(bindings, frame -> {
            callbackThread.set(Thread.currentThread().getName());
            frames.incrementAndGet();
            delivered.countDown();
        }, executor);
        core.start(session("instrumented-frame"));

        bindings.callbacks.onFrame(new byte[] { 1 }, 2, 1, 1);
        delivered.await();
        assertEquals("beacon-device-frame", callbackThread.get());
        assertNotEquals(Thread.currentThread().getName(), callbackThread.get());
        core.close();
        bindings.callbacks.onFrame(new byte[] { 2 }, 3, 2, 1);
        assertEquals(1, frames.get());
    }

    @Test
    public void testConnectionLossCleanupReleasesWithoutDuplicateStop() {
        RecordingBindings bindings = new RecordingBindings();
        ExecutorService executor = Executors.newSingleThreadExecutor();
        BeaconStreamCore core = new BeaconStreamCore(bindings, frame -> { }, executor);
        core.start(session("instrumented-loss"));
        bindings.callbacks.onConnectionLost(1);
        core.close();
        core.close();
        assertEquals(0, bindings.stopCount);
        assertEquals(1, bindings.releaseCount);
    }

    @Test
    public void testCloseDrainsInflightSinkAndDiscardsQueuedFrame() throws InterruptedException {
        RecordingBindings bindings = new RecordingBindings();
        CountDownLatch sinkEntered = new CountDownLatch(1);
        CountDownLatch releaseSink = new CountDownLatch(1);
        CountDownLatch closeStarted = new CountDownLatch(1);
        CountDownLatch closeFinished = new CountDownLatch(1);
        AtomicInteger frames = new AtomicInteger();
        BeaconStreamCore core = new BeaconStreamCore(
            bindings,
            frame -> {
                frames.incrementAndGet();
                sinkEntered.countDown();
                try {
                    releaseSink.await();
                } catch (InterruptedException error) {
                    Thread.currentThread().interrupt();
                    throw new AssertionError(error);
                }
            },
            Executors.newSingleThreadExecutor());
        core.start(session("instrumented-drain"));
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1, 1);
        sinkEntered.await();
        bindings.callbacks.onFrame(new byte[] { 2 }, 2, 2, 1);
        Thread closer = new Thread(() -> {
            closeStarted.countDown();
            core.close();
            closeFinished.countDown();
        });
        closer.start();
        closeStarted.await();
        assertEquals(0, bindings.releaseCount);
        releaseSink.countDown();
        closeFinished.await();
        closer.join();
        assertEquals(1, frames.get());
        assertEquals(1, bindings.releaseCount);
    }

    @Test
    public void testNativeLoadCreateSurfaceStopRelease() {
        BeaconStreamCore core = new BeaconStreamCore(frame -> { });
        SurfaceTexture texture = new SurfaceTexture(0);
        Surface first = new Surface(texture);
        Surface second = new Surface(texture);
        core.replaceSurface(first);
        core.replaceSurface(second);
        core.replaceSurface(null);
        core.stop();
        core.stop();
        core.close();
        core.close();
        first.release();
        second.release();
        texture.release();
    }

    @Test
    public void testProductionJniParsesBenchmarkGrantWithoutVideoMode() {
        BeaconStreamCore core = new BeaconStreamCore(frame -> { });
        BeaconStreamSession session = BeaconStreamSession.parse(
            "https://127.0.0.1",
            "z-fold-7",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":9,\"planExplanation\":\"preflight\",\"sessionId\":\"benchmark:3c13df40-26c4-40c6-8414-268734f1024d\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"benchmark\":{\"runId\":\"3c13df40-26c4-40c6-8414-268734f1024d\",\"schemaVersion\":1,\"reliableRound\":{\"packetCount\":16,\"payloadBytes\":32768,\"measurementIntervalUs\":250000},\"datagramRound\":{\"packetCount\":64,\"payloadBytes\":1000,\"measurementIntervalUs\":250000},\"runToken\":\"AAECAwQFBgcICQoLDA0ODw==\"}}}");
        BeaconStreamSession.NativeGrant grant = session.consumeNativeGrant(1);
        try {
            assertTrue(BeaconStreamCore.parseNativeGrantForTest(grant));
        } finally {
            grant.clearSecrets();
            core.close();
        }
    }

    @Test
    public void testProductionJniRejectsNullInputFieldsWithoutCheckJniAbort() {
        BeaconStreamCore core = new BeaconStreamCore(frame -> { });
        assertThrows(IllegalArgumentException.class, () -> core.sendInput(null));

        BeaconApiClient.InputBatch nullEvents = new BeaconApiClient.InputBatch();
        assertThrows(IllegalArgumentException.class, () -> core.sendInput(nullEvents));

        BeaconApiClient.InputBatch nullEvent = new BeaconApiClient.InputBatch();
        nullEvent.events = new BeaconApiClient.InputEvent[] { null };
        assertThrows(IllegalArgumentException.class, () -> core.sendInput(nullEvent));

        BeaconApiClient.InputBatch nullCoordinates = new BeaconApiClient.InputBatch();
        BeaconApiClient.InputEvent pointer = new BeaconApiClient.InputEvent();
        pointer.type = "pointer";
        pointer.action = "move";
        nullCoordinates.events = new BeaconApiClient.InputEvent[] { pointer };
        assertThrows(IllegalArgumentException.class, () -> core.sendInput(nullCoordinates));
        core.close();
    }

    @Test
    public void testNativeThreadAttachmentCallbackExceptionAndRegistryDrain()
        throws InterruptedException {
        CountDownLatch callbackEntered = new CountDownLatch(1);
        BeaconStreamCore.NativeCallbacks callbacks = new BeaconStreamCore.NativeCallbacks() {
            @Override public void onFrame(
                byte[] bytes, long presentationTimeUs, long sequence, long generation) {
                callbackEntered.countDown();
                throw new IllegalStateException("instrumented callback failure");
            }

            @Override public void onConnectionLost(long generation) {
                throw new IllegalStateException("instrumented loss failure");
            }
        };
        long handle = BeaconStreamCore.createNativeHandleForTest(callbacks);
        assertTrue(handle != 0);
        BeaconStreamCore.emitNativeFrameForTest(handle, new byte[] { 1, 2, 3 }, 7);
        callbackEntered.await();
        BeaconStreamCore.releaseNativeHandleForTest(handle);
        BeaconStreamCore.awaitNativeRegistryIdleForTest();
        assertEquals(0, BeaconStreamCore.nativeRegistrySizeForTest());
    }

    @Test
    public void testNativeBenchmarkResultCrossesJniAndRegistryDrains()
        throws InterruptedException {
        BeaconStreamCore loader = new BeaconStreamCore(frame -> { });
        CountDownLatch callbackEntered = new CountDownLatch(1);
        BeaconStreamCore.NativeCallbacks callbacks = new BeaconStreamCore.NativeCallbacks() {
            @Override public void onFrame(
                byte[] bytes, long presentationTimeUs, long sequence, long generation) { }

            @Override public void onConnectionLost(long generation) { }

            @Override public void onBenchmarkCompleted(
                double sustainableThroughputMbps,
                long[] sequences,
                int[] payloadBytes,
                long[] rttUs,
                long[] jitterUs,
                int[] reorderDistances,
                boolean[] received,
                long generation) {
                assertEquals(96.5, sustainableThroughputMbps, 0.001);
                assertEquals(2, sequences.length);
                assertEquals(1, sequences[1]);
                assertEquals(1000, payloadBytes[1]);
                assertEquals(2500, rttUs[1]);
                assertEquals(300, jitterUs[1]);
                assertEquals(1, reorderDistances[1]);
                assertTrue(!received[1]);
                assertEquals(7, generation);
                callbackEntered.countDown();
            }
        };
        long handle = BeaconStreamCore.createNativeHandleForTest(callbacks);
        assertTrue(handle != 0);

        BeaconStreamCore.emitNativeBenchmarkResultForTest(handle, 7);

        callbackEntered.await();
        BeaconStreamCore.releaseNativeHandleForTest(handle);
        loader.close();
        BeaconStreamCore.awaitNativeRegistryIdleForTest();
        assertEquals(0, BeaconStreamCore.nativeRegistrySizeForTest());
    }

    @Test
    public void testActivityDestructionClosesOwnedSession() throws InterruptedException {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Intent intent = new Intent(instrumentation.getTargetContext(), BeaconActivity.class)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        BeaconActivity activity = (BeaconActivity) instrumentation.startActivitySync(intent);
        AtomicReference<BeaconViewModel> model = new AtomicReference<>();
        instrumentation.runOnMainSync(
            () -> model.set(activity.createOwnedModelForInstrumentation()));
        BeaconStreamCore ownedCore = model.get().ownedStreamCore();
        assertTrue(ownedCore.isOpen());
        CountDownLatch destroyed = new CountDownLatch(1);
        ActivityLifecycleMonitor monitor = ActivityLifecycleMonitorRegistry.getInstance();
        ActivityLifecycleCallback callback = (candidate, stage) -> {
            if (candidate == activity && stage == Stage.DESTROYED) {
                destroyed.countDown();
            }
        };
        monitor.addLifecycleCallback(callback);
        instrumentation.runOnMainSync(activity::finish);
        destroyed.await();
        monitor.removeLifecycleCallback(callback);
        assertTrue(activity.isDestroyed());
        assertTrue(!ownedCore.isOpen());
        assertTrue(ownedCore.callbackExecutorShutdown());
        assertTrue(activity.workerExecutorShutdown());
    }

    @Test
    public void gate3ConnectSendAndDisconnect() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Bundle arguments = requireGate3Arguments();
        String serverUrl = requireArgument(arguments, "serverUrl");
        String clientId = requireArgument(arguments, "clientId");
        String inputMarker = requireArgument(arguments, "inputMarker");
        installCredential(instrumentation, clientId);
        Gate3SessionEvidence evidence = Gate3SessionEvidence.startFirstInvocation(
            instrumentation.getTargetContext(), clientId);
        BeaconStreamCore core = new BeaconStreamCore(
            evidence, evidence::recordFeedbackSent, evidence::recordStreamFailure);
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), new BeaconClientConfig(serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(clientId, serverUrl, api, core);
        try {
            registerAndLaunch(model);
            evidence.recordGrant(model.latestStream());
            evidence.awaitMarkerAndFeedback();
            model.sendInput(BeaconApiClient.InputBatch.keyboardPress(1, inputMarker, "Escape"));
            evidence.recordInputSent();
            evidence.persistForReconnect();
        } finally {
            model.close();
            BeaconStreamCore.awaitNativeRegistryIdleForTest();
            evidence.recordTransportClosedAfterNativeDrain();
        }

        assertFirstInvocationEvidence(evidence);
        emit("BEACON_GATE3_READY");
        emit("BEACON_GATE3_FRAME 1");
        emit("BEACON_GATE3_INPUT_ECHO 1");
        emit("BEACON_GATE3_FEEDBACK 1");
    }

    @Test
    public void gate3EvidenceFailsClosedOnPreMarkerTransportLoss() {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Gate3SessionEvidence evidence = Gate3SessionEvidence.startFirstInvocation(
            instrumentation.getTargetContext(), "gate3-failure-fixture");

        evidence.recordStreamFailure("transport");

        AssertionError error = assertThrows(
            AssertionError.class, evidence::awaitMarkerAndFeedback);
        assertTrue(error.getMessage().contains("transport"));
    }

    @Test
    public void gate3ReconnectAndStop() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Bundle arguments = requireGate3Arguments();
        String serverUrl = requireArgument(arguments, "serverUrl");
        String clientId = requireArgument(arguments, "clientId");
        installCredential(instrumentation, clientId);
        Gate3SessionEvidence.PreviousInvocation previous = Gate3SessionEvidence.loadPrevious(
            instrumentation.getTargetContext(), clientId);
        Gate3SessionEvidence evidence = Gate3SessionEvidence.startReconnect(
            instrumentation.getTargetContext(), clientId);
        BeaconStreamCore core = new BeaconStreamCore(
            evidence, evidence::recordFeedbackSent, evidence::recordStreamFailure);
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), new BeaconClientConfig(serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(clientId, serverUrl, api, core);
        try {
            model.reconnect();
            assertSuccessful(model);
            evidence.recordGrant(model.latestStream());
            evidence.awaitMarkerAndFeedback();
            evidence.assertFreshReconnect(previous);
            model.stopStream();
            assertSuccessful(model);
        } finally {
            model.close();
            BeaconStreamCore.awaitNativeRegistryIdleForTest();
            evidence.recordTransportClosedAfterNativeDrain();
        }

        assertTrue(evidence.transportConnected());
        assertEquals(1L, evidence.receivedFrameCount());
        assertTrue(evidence.feedbackSent());
        assertTrue(evidence.transportClosed());
        evidence.clearPersistedReconnect();
        emit("BEACON_GATE3_RECONNECT_FRESH_TICKET");
    }

    @Test
    public void gate3ConnectAndAwaitWorkerCrash() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Bundle arguments = requireGate3Arguments();
        String serverUrl = requireArgument(arguments, "serverUrl");
        String clientId = requireArgument(arguments, "clientId");
        installCredential(instrumentation, clientId);
        Gate3SessionEvidence evidence = Gate3SessionEvidence.startReconnect(
            instrumentation.getTargetContext(), clientId);
        BeaconStreamCore core = new BeaconStreamCore(
            evidence, evidence::recordFeedbackSent, evidence::recordStreamFailure);
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), new BeaconClientConfig(serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(clientId, serverUrl, api, core);
        try {
            registerAndLaunch(model);
            evidence.recordGrant(model.latestStream());
            evidence.awaitMarkerAndFeedback();
            emit("BEACON_GATE3_WORKER_CRASH_ARMED");
            core.awaitConnectionLossForTest(1);
            assertEquals(1, core.connectionLossCountForTest());
            assertTrue(core.stoppedForTest());
        } finally {
            model.close();
            BeaconStreamCore.awaitNativeRegistryIdleForTest();
            evidence.recordTransportClosedAfterNativeDrain();
        }

        assertTrue(evidence.transportConnected());
        assertEquals(1L, evidence.receivedFrameCount());
        assertTrue(evidence.feedbackSent());
        assertTrue(evidence.transportClosed());
        emit("BEACON_GATE3_WORKER_CRASH_OBSERVED");
    }

    private static String requireArgument(Bundle arguments, String name) {
        String value = arguments.getString(name);
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException(
                "Missing required instrumentation argument: " + name);
        }
        return value.trim();
    }

    private static Bundle requireGate3Arguments() {
        Bundle arguments = InstrumentationRegistry.getArguments();
        String serverUrl = arguments.getString("serverUrl");
        assumeTrue(
            "Gate 3 host-dependent instrumentation requires an explicit serverUrl.",
            serverUrl != null && !serverUrl.trim().isEmpty());
        return arguments;
    }

    private static void installCredential(
        Instrumentation instrumentation,
        String clientId) {
        AndroidKeyStoreCredentialStore store = new AndroidKeyStoreCredentialStore(
            instrumentation.getTargetContext(), clientId);
        File evidence = new File(
            instrumentation.getTargetContext().getFilesDir(), CredentialEvidenceFile);
        if (!evidence.exists()) {
            assertTrue("Gate 3 credential is unavailable.", store.loadCredential() != null);
            return;
        }

        long length = evidence.length();
        if (length <= 0 || length > 1024) {
            throw new IllegalStateException("Gate 3 credential evidence has an invalid size.");
        }
        byte[] bytes = new byte[(int) length];
        try (FileInputStream input = new FileInputStream(evidence)) {
            int offset = 0;
            while (offset < bytes.length) {
                int read = input.read(bytes, offset, bytes.length - offset);
                if (read < 0) {
                    throw new IOException("Unexpected end of credential evidence.");
                }
                offset += read;
            }
            store.saveCredential(new String(bytes, StandardCharsets.UTF_8));
        } catch (IOException error) {
            throw new AssertionError("Could not read private Gate 3 credential evidence.", error);
        } finally {
            Arrays.fill(bytes, (byte) 0);
            if (evidence.exists() && !evidence.delete()) {
                throw new IllegalStateException("Could not delete Gate 3 credential evidence.");
            }
        }
    }

    private static void registerAndLaunch(BeaconViewModel model) throws Exception {
        model.refresh();
        assertSuccessful(model);
        model.beacon(true);
        assertSuccessful(model);
        model.reportCapabilities(gate3Capabilities());
        assertSuccessful(model);
        model.reportTelemetry(gate3Telemetry());
        assertSuccessful(model);
        model.requestPlan(gate3Game());
        assertSuccessful(model);
        model.launch(gate3Game());
        assertSuccessful(model);
    }

    private static void assertFirstInvocationEvidence(Gate3SessionEvidence evidence) {
        assertTrue(evidence.transportConnected());
        assertEquals(1L, evidence.receivedFrameCount());
        assertEquals(1L, evidence.markerSequence());
        assertTrue(evidence.inputSent());
        assertTrue(evidence.feedbackSent());
        assertTrue(evidence.transportClosed());
    }

    private static void emit(String marker) {
        Log.i("BeaconGate3", marker);
        System.out.println(marker);
    }

    private static void assertSuccessful(BeaconViewModel model) {
        assertTrue(model.status(), model.status().matches(".*: 2[0-9][0-9]"));
        assertTrue(model.latestError(), model.latestError().isEmpty());
    }

    private static BeaconApiClient.ClientCapabilities gate3Capabilities() {
        return new BeaconApiClient.ClientCapabilities(
            false, false, true, false, false, 60, true, "1280x720@60");
    }

    private static BeaconApiClient.ClientTelemetry gate3Telemetry() {
        return new BeaconApiClient.ClientTelemetry(
            1, 0, 1, 1000, "emulator", 100, "nominal");
    }

    private static BeaconApiClient.GameSelection gate3Game() {
        return BeaconApiClient.GameSelection.byGameId("steam-shortcut:3767414131");
    }

    private static BeaconStreamSession session(String sessionId) {
        return BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":1,\"planExplanation\":\"selected\",\"sessionId\":\"" + sessionId + "\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1280,\"height\":720,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"}}}");
    }

    private static final class RecordingBindings implements BeaconStreamCore.Bindings {
        BeaconStreamCore.NativeCallbacks callbacks;
        int stopCount;
        int releaseCount;

        @Override public long create(BeaconStreamCore.NativeCallbacks value) { callbacks = value; return 11; }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) {
            return true;
        }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) { }
        @Override public void replaceSurface(long handle, Object surface) { }
        @Override public void stop(long handle) { stopCount++; }
        @Override public void release(long handle) { releaseCount++; }
    }
}
