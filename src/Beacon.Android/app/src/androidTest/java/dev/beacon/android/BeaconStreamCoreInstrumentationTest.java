package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Context;
import android.content.Intent;
import android.graphics.SurfaceTexture;
import android.os.Bundle;
import android.util.Log;
import android.view.Surface;
import android.view.SurfaceView;

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
import java.nio.ByteBuffer;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertNull;

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

        bindings.callbacks.onFrame(directBuffer(1), 2, 1, 1, false, false);
        delivered.await();
        assertEquals("beacon-device-frame", callbackThread.get());
        assertNotEquals(Thread.currentThread().getName(), callbackThread.get());
        core.close();
        bindings.callbacks.onFrame(directBuffer(2), 3, 2, 1, false, false);
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
        bindings.callbacks.onFrame(directBuffer(1), 1, 1, 1, false, false);
        sinkEntered.await();
        bindings.callbacks.onFrame(directBuffer(2), 2, 2, 1, false, false);
        Thread closer = new Thread(() -> {
            core.close();
            closeFinished.countDown();
        });
        closer.start();
        bindings.stopEntered.await();
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
    public void testProductionJniMapsControllerInput() {
        int[] values = BeaconStreamCore.parseNativeControllerInputForTest(
            BeaconApiClient.InputBatch.controller(9, 0, 12, 1));

        assertEquals(3, values.length);
        assertEquals(0, values[0]);
        assertEquals(12, values[1]);
        assertEquals(1, values[2]);
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
                ByteBuffer bytes,
                long presentationTimeUs,
                long sequence,
                long generation,
                boolean idr,
                boolean codecConfiguration) {
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
                ByteBuffer bytes,
                long presentationTimeUs,
                long sequence,
                long generation,
                boolean idr,
                boolean codecConfiguration) { }

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
    public void testNativeAudioPcmCrossesJniAndRegistryDrains()
        throws InterruptedException {
        BeaconStreamCore loader = new BeaconStreamCore(frame -> { });
        CountDownLatch callbackEntered = new CountDownLatch(1);
        AtomicReference<ByteBuffer> observed = new AtomicReference<>();
        BeaconStreamCore.NativeCallbacks callbacks = new BeaconStreamCore.NativeCallbacks() {
            @Override public void onFrame(
                ByteBuffer bytes,
                long presentationTimeUs,
                long sequence,
                long generation,
                boolean idr,
                boolean codecConfiguration) { }

            @Override public void onAudioPcm(
                ByteBuffer pcm,
                long presentationTimeUs,
                long sequence,
                long generation,
                boolean concealed) {
                assertEquals(20_000, presentationTimeUs);
                assertEquals(1, sequence);
                assertTrue(!concealed);
                observed.set(pcm);
                callbackEntered.countDown();
            }

            @Override public void onConnectionLost(long generation) { }
        };
        long handle = BeaconStreamCore.createNativeHandleForTest(callbacks);
        assertTrue(handle != 0);
        float[] pcm = new float[1_920];
        pcm[0] = 0.25F;

        BeaconStreamCore.emitNativeAudioPcmForTest(handle, pcm, 20_000);

        callbackEntered.await();
        assertNotNull(observed.get());
        assertEquals(1_920 * Float.BYTES, observed.get().remaining());
        BeaconStreamCore.releaseNativeHandleForTest(handle);
        loader.close();
        BeaconStreamCore.awaitNativeRegistryIdleForTest();
        assertEquals(0, BeaconStreamCore.nativeRegistrySizeForTest());
    }

    @Test
    public void testProductionAudioTrackAcceptsOneBeaconPcmFrame() {
        AtomicReference<Throwable> failure = new AtomicReference<>();
        BeaconAudioSession session = new BeaconAudioSession(failure::set);
        ByteBuffer pcm = ByteBuffer.allocateDirect(1_920 * Float.BYTES)
            .order(java.nio.ByteOrder.nativeOrder());
        pcm.putFloat(0.1F);
        pcm.position(0);

        session.start(
            1,
            new BeaconStreamSession.SelectedAudio(
                "opus", 48_000, 2, 20_000, 96_000));
        session.onAudioPcm(new BeaconStreamCore.DecodedAudioFrame(
            pcm, 20_000, 1, false));
        session.stop();
        session.close();

        assertNull(failure.get());
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
        AtomicReference<BeaconStreamCore> coreReference = new AtomicReference<>();
        Gate3VideoRuntime videoRuntime = new Gate3VideoRuntime(evidence);
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), productionClientConfig(arguments, serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(
            clientId,
            serverUrl,
            api,
            gate3StreamCoreFactory(evidence, coreReference),
            videoRuntime::createSession);
        try {
            registerAndLaunch(model);
            evidence.recordGrant(model.latestStream());
            evidence.awaitRenderedFrameFeedback();
            model.sendInput(BeaconApiClient.InputBatch.keyboardPress(1, inputMarker, "Escape"));
            evidence.recordInputSent();
            evidence.persistForReconnect();
        } finally {
            try {
                model.close();
                BeaconStreamCore.awaitNativeRegistryIdleForTest();
                evidence.recordTransportClosedAfterNativeDrain();
            } finally {
                videoRuntime.close();
            }
        }

        assertFirstInvocationEvidence(evidence);
        emit("BEACON_GATE3_READY");
        emit("BEACON_GATE3_FRAME 1");
        emit("BEACON_GATE3_INPUT_ECHO 1");
        emit("BEACON_GATE3_FEEDBACK 1");
    }

    @Test
    public void gate3EvidenceFailsClosedOnPreFrameTransportLoss() {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Gate3SessionEvidence evidence = Gate3SessionEvidence.startFirstInvocation(
            instrumentation.getTargetContext(), "gate3-failure-fixture");

        evidence.recordStreamFailure("transport");

        AssertionError error = assertThrows(
            AssertionError.class, evidence::awaitRenderedFrameFeedback);
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
        AtomicReference<BeaconStreamCore> coreReference = new AtomicReference<>();
        Gate3VideoRuntime videoRuntime = new Gate3VideoRuntime(evidence);
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), productionClientConfig(arguments, serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(
            clientId,
            serverUrl,
            api,
            gate3StreamCoreFactory(evidence, coreReference),
            videoRuntime::createSession);
        try {
            model.reconnect();
            assertSuccessful(model);
            evidence.recordGrant(model.latestStream());
            evidence.awaitRenderedFrameFeedback();
            evidence.assertFreshReconnect(previous);
            model.stopStream();
            assertSuccessful(model);
        } finally {
            try {
                model.close();
                BeaconStreamCore.awaitNativeRegistryIdleForTest();
                evidence.recordTransportClosedAfterNativeDrain();
            } finally {
                videoRuntime.close();
            }
        }

        assertTrue(evidence.transportConnected());
        assertTrue(evidence.receivedFrameCount() >= 1L);
        assertTrue(evidence.feedbackSent());
        assertTrue(evidence.surfacePresented());
        assertTrue(evidence.transportClosed());
        evidence.clearPersistedReconnect();
        emit("BEACON_GATE3_RECONNECT_FRESH_TICKET");
    }

    @Test
    public void gate5ProductionConnectSendAndDisconnect() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Bundle arguments = requireGate3Arguments();
        String serverUrl = requireArgument(arguments, "serverUrl");
        String clientId = requireArgument(arguments, "clientId");
        String gameId = requireArgument(arguments, "gameId");
        installCredential(instrumentation, clientId);
        Gate3SessionEvidence evidence = Gate3SessionEvidence.startFirstInvocation(
            instrumentation.getTargetContext(), clientId);
        AtomicReference<BeaconStreamCore> coreReference = new AtomicReference<>();
        Gate5VideoRuntime videoRuntime = new Gate5VideoRuntime(instrumentation, evidence);
        Gate5AudioRuntime audioRuntime = new Gate5AudioRuntime();
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), productionClientConfig(arguments, serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(
            clientId,
            serverUrl,
            api,
            gate5StreamCoreFactory(evidence, videoRuntime, coreReference),
            videoRuntime::createSession,
            audioRuntime::createSession);
        try {
            registerAndLaunch(model, BeaconApiClient.GameSelection.byGameId(gameId));
            evidence.recordGrant(model.latestStream());
            videoRuntime.awaitChangingFrames();
            model.sendInput(BeaconApiClient.InputBatch.keyboardPress(1, "F12", "F12"));
            model.sendInput(BeaconApiClient.InputBatch.controller(2, 0, 12, 1));
            evidence.recordInputSent();
            evidence.persistForReconnect();
            model.disconnect();
            assertSuccessful(model);
            audioRuntime.assertPlayback();
        } finally {
            try {
                model.close();
                BeaconStreamCore.awaitNativeRegistryIdleForTest();
                evidence.recordTransportClosedAfterNativeDrain();
            } finally {
                videoRuntime.close();
            }
        }

        assertFirstInvocationEvidence(evidence);
        emit("BEACON_GATE5_MOVING_FRAMES " + videoRuntime.frameCount());
        emit("BEACON_GATE5_PIXEL_VARIANTS " + videoRuntime.pixelVariantCount());
        emit("BEACON_GATE5_AUDIO_PCM_WRITTEN " + audioRuntime.writtenFrameCount());
        emit("BEACON_GATE5_INPUT_SENT F12");
        emit("BEACON_GATE5_CONTROLLER_SENT A_DOWN");
        emit("BEACON_GATE5_ACTIVE_DISCONNECT");
    }

    @Test
    public void gate5ProductionReconnectAndQuit() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Bundle arguments = requireGate3Arguments();
        String serverUrl = requireArgument(arguments, "serverUrl");
        String clientId = requireArgument(arguments, "clientId");
        installCredential(instrumentation, clientId);
        Gate3SessionEvidence.PreviousInvocation previous = Gate3SessionEvidence.loadPrevious(
            instrumentation.getTargetContext(), clientId);
        Gate3SessionEvidence evidence = Gate3SessionEvidence.startReconnect(
            instrumentation.getTargetContext(), clientId);
        AtomicReference<BeaconStreamCore> coreReference = new AtomicReference<>();
        Gate5VideoRuntime videoRuntime = new Gate5VideoRuntime(instrumentation, evidence);
        Gate5AudioRuntime audioRuntime = new Gate5AudioRuntime();
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), productionClientConfig(arguments, serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(
            clientId,
            serverUrl,
            api,
            gate5StreamCoreFactory(evidence, videoRuntime, coreReference),
            videoRuntime::createSession,
            audioRuntime::createSession);
        try {
            model.reconnect();
            assertSuccessful(model);
            evidence.recordGrant(model.latestStream());
            videoRuntime.awaitChangingFrames();
            evidence.assertFreshReconnect(previous);
            model.sendInput(BeaconApiClient.InputBatch.controller(3, 0, 12, 0));
            model.quit(new BeaconApiClient.QuitState(false));
            assertSuccessful(model);
            audioRuntime.assertPlayback();
        } finally {
            try {
                model.close();
                BeaconStreamCore.awaitNativeRegistryIdleForTest();
                evidence.recordTransportClosedAfterNativeDrain();
            } finally {
                videoRuntime.close();
            }
        }

        assertTrue(evidence.transportConnected());
        assertTrue(evidence.receivedFrameCount() >= 1L);
        assertTrue(evidence.feedbackSent());
        assertTrue(evidence.surfacePresented());
        assertTrue(evidence.transportClosed());
        evidence.clearPersistedReconnect();
        emit("BEACON_GATE5_RECONNECT_FRESH_TICKET");
        emit("BEACON_GATE5_RECONNECT_AUDIO_PCM_WRITTEN " + audioRuntime.writtenFrameCount());
        emit("BEACON_GATE5_CONTROLLER_SENT A_UP");
        emit("BEACON_GATE5_QUIT_INACTIVE");
    }

    @Test
    public void gate4NetworkAndHardwareBenchmark() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Gate4BenchmarkOutcome outcome = runGate4Benchmark(
            instrumentation,
            AndroidDeviceBenchmarkRunner.system(instrumentation.getTargetContext()),
            true);
        assertNotNull(outcome.networkEvidence);
        emit("BEACON_GATE4_NATIVE_NETWORK_COMPLETE");
        emit("BEACON_HARDWARE_EVIDENCE " + decoderEvidence(outcome.deviceEvidence));
        boolean expectedRejection = outcome.completion.statusCode() == 400 && (
            outcome.completion.body().contains("No sustainable decoder candidate is available.") ||
            outcome.completion.body().contains(
                "Measured throughput cannot sustain the minimum 5 Mbps initial bitrate."));
        assertTrue(
            outcome.completion.body() + " device=" + decoderEvidence(outcome.deviceEvidence),
            outcome.completion.isSuccess() || expectedRejection);
        emit(expectedRejection
            ? "BEACON_GATE4_REAL_HARDWARE_CAPABILITY_REJECTED"
            : "BEACON_GATE4_REAL_HARDWARE_ACCEPTED");
        emit("BEACON_GATE4_REAL_HARDWARE_OBSERVED");
    }

    @Test
    public void gate4DefaultNetworkChangeMonitor() throws Exception {
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        CountDownLatch changed = new CountDownLatch(1);
        AtomicReference<AndroidBenchmarkNetworkState> observed = new AtomicReference<>();
        AndroidBenchmarkChangeMonitor monitor = new AndroidBenchmarkChangeMonitor(
            instrumentation.getTargetContext());
        try {
            monitor.start(network -> {
                observed.set(network);
                changed.countDown();
            });
            changed.await();
        } finally {
            monitor.close();
        }

        assertNotNull(observed.get());
        assertTrue(!"none".equals(observed.get().transport()));
        assertTrue(!observed.get().localNetworkPrefix().isEmpty());
        emit("BEACON_GATE4_CHANGE_MONITOR");
    }

    @Test
    public void gate4CertifiedBenchmarkEvidence() throws Exception {
        Gate4BenchmarkOutcome outcome = runGate4Benchmark(
            InstrumentationRegistry.getInstrumentation(),
            certifiedDeviceRunner(),
            false);
        assertTrue(outcome.completion.body(), outcome.completion.isSuccess());
        emit("BEACON_GATE4_BENCHMARK_COMPLETE");
        emit("BEACON_GATE4_CERTIFIED_MANUAL_COMPLETE");
    }

    @Test
    public void gate4CertifiedSessionPreflight() throws Exception {
        Gate4BenchmarkOutcome outcome = runGate4Benchmark(
            InstrumentationRegistry.getInstrumentation(),
            certifiedNetworkOnlyDeviceRunner(),
            false,
            "sessionPreflight");
        assertTrue(outcome.completion.body(), outcome.completion.isSuccess());
        assertTrue(outcome.deviceEvidence.decoderSamples().isEmpty());
        assertEquals(1, outcome.deviceEvidence.powerSamples().size());
        emit("BEACON_GATE4_SESSION_PREFLIGHT");
        emit("BEACON_GATE4_CERTIFIED_PREFLIGHT_COMPLETE");
    }

    private static Gate4BenchmarkOutcome runGate4Benchmark(
        Instrumentation instrumentation,
        BeaconDeviceBenchmarkRunner deviceRunner,
        boolean useNativeNetwork) throws Exception {
        return runGate4Benchmark(instrumentation, deviceRunner, useNativeNetwork, "manual");
    }

    private static Gate4BenchmarkOutcome runGate4Benchmark(
        Instrumentation instrumentation,
        BeaconDeviceBenchmarkRunner deviceRunner,
        boolean useNativeNetwork,
        String trigger) throws Exception {
        Bundle arguments = requireGate3Arguments();
        String serverUrl = requireArgument(arguments, "serverUrl");
        String clientId = requireArgument(arguments, "clientId");
        String serverPublicKeyFingerprint =
            requireArgument(arguments, "serverPublicKeyFingerprint");
        installCredential(instrumentation, clientId);
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(),
            new BeaconClientConfig(serverUrl, clientId, serverPublicKeyFingerprint));
        assertTrue(api.hello().isSuccess());
        assertTrue(api.reportCapabilities(gate3Capabilities()).isSuccess());

        CountDownLatch finished = new CountDownLatch(1);
        AtomicReference<BeaconApiClient.BeaconResult> completion = new AtomicReference<>();
        AtomicReference<Throwable> failure = new AtomicReference<>();
        AtomicReference<BeaconStreamCore.BenchmarkNetworkResult> networkEvidence =
            new AtomicReference<>();
        AtomicReference<BeaconBenchmarkDeviceEvidence> deviceEvidence = new AtomicReference<>();
        AtomicReference<BeaconBenchmarkCoordinator> coordinatorReference = new AtomicReference<>();
        AtomicReference<BeaconStreamCore> coreReference = new AtomicReference<>();
        if (useNativeNetwork) {
            coreReference.set(new BeaconStreamCore(
                frame -> { },
                () -> { },
                stage -> coordinatorReference.get().onStreamCoreFailure(stage),
                result -> reportNetworkEvidence(
                    result,
                    networkEvidence,
                    coordinatorReference)));
        }
        BeaconBenchmarkCoordinator coordinator = new BeaconBenchmarkCoordinator(
            api,
            new BeaconBenchmarkCoordinator.ResultObserver() {
                @Override
                public void onResult(String action, BeaconApiClient.BeaconResult result) {
                    if ("benchmark complete".equals(action)) {
                        completion.set(result);
                        finished.countDown();
                    }
                }

                @Override
                public void onFailure(String action, Throwable value) {
                    failure.set(value);
                    finished.countDown();
                }
            });
        coordinatorReference.set(coordinator);
        BeaconBenchmarkCoordinator.StreamController stream =
            new BeaconBenchmarkCoordinator.StreamController() {
                @Override
                public void start(String responseBody) {
                    BeaconStreamSession session =
                        BeaconStreamSession.parse(serverUrl, clientId, responseBody);
                    if (useNativeNetwork) {
                        coreReference.get().start(session);
                    } else {
                        reportNetworkEvidence(
                            certifiedNetworkResult(session.benchmark()),
                            networkEvidence,
                            coordinatorReference);
                    }
                }

                @Override
                public void stop() {
                    BeaconStreamCore core = coreReference.get();
                    if (core != null) core.stop();
                }
        };
        BeaconDeviceBenchmarkRunner observedRunner = (plan, observer) ->
            deviceRunner.start(plan, new BeaconDeviceBenchmarkRunner.Observer() {
                @Override
                public void onCompleted(BeaconBenchmarkDeviceEvidence evidence) {
                    deviceEvidence.set(evidence);
                    observer.onCompleted(evidence);
                }

                @Override
                public void onFailure(Throwable value) {
                    observer.onFailure(value);
                }
            });
        try {
            coordinator.run(
                gate4BenchmarkRequest(
                    instrumentation.getTargetContext(),
                    serverUrl,
                    trigger),
                observedRunner,
                stream);
            finished.await();
            if (failure.get() != null) {
                throw new AssertionError("Gate 4 benchmark failed.", failure.get());
            }
            assertNotNull(completion.get());
            return new Gate4BenchmarkOutcome(
                completion.get(),
                networkEvidence.get(),
                deviceEvidence.get());
        } finally {
            BeaconStreamCore core = coreReference.get();
            if (core != null) {
                core.close();
                BeaconStreamCore.awaitNativeRegistryIdleForTest();
            }
        }
    }

    private static void reportNetworkEvidence(
        BeaconStreamCore.BenchmarkNetworkResult result,
        AtomicReference<BeaconStreamCore.BenchmarkNetworkResult> evidence,
        AtomicReference<BeaconBenchmarkCoordinator> coordinator) {
        evidence.set(result);
        emit("BEACON_NETWORK_EVIDENCE throughputMbps=" +
            result.sustainableThroughputMbps +
            " received=" + result.samples.stream()
                .filter(sample -> sample.received)
                .count() +
            " expected=" + result.samples.size());
        coordinator.get().onNetworkCompleted(result);
    }

    private static BeaconStreamCore.BenchmarkNetworkResult certifiedNetworkResult(
        BeaconStreamSession.Benchmark benchmark) {
        assertNotNull(benchmark);
        List<BeaconStreamCore.BenchmarkNetworkSample> samples = new ArrayList<>();
        BeaconStreamSession.BenchmarkRound datagram = benchmark.datagramRound();
        for (int sequence = 0; sequence < datagram.packetCount(); sequence++) {
            samples.add(new BeaconStreamCore.BenchmarkNetworkSample(
                sequence,
                datagram.payloadBytes(),
                8_000,
                1_000,
                0,
                true));
        }
        return new BeaconStreamCore.BenchmarkNetworkResult(100.0, samples);
    }

    private static BeaconDeviceBenchmarkRunner certifiedDeviceRunner() {
        return (plan, observer) -> {
            List<BeaconBenchmarkCompletionRequest.DecoderSample> decoders =
                new ArrayList<>();
            List<BeaconBenchmarkCompletionRequest.PowerSample> power =
                new ArrayList<>();
            for (BeaconBenchmarkHardwarePlan.DecoderRound round : plan.decoderRounds()) {
                power.add(new BeaconBenchmarkCompletionRequest.PowerSample(
                    100,
                    true,
                    "nominal"));
                decoders.add(new BeaconBenchmarkCompletionRequest.DecoderSample(
                    round.codec(),
                    round.profile(),
                    round.bitDepth(),
                    round.width(),
                    round.height(),
                    round.targetFps(),
                    true,
                    round.targetFps(),
                    1.0,
                    2.0,
                    0,
                    0,
                    false,
                    false));
                power.add(new BeaconBenchmarkCompletionRequest.PowerSample(
                    100,
                    true,
                    "nominal"));
            }
            observer.onCompleted(new BeaconBenchmarkDeviceEvidence(decoders, power));
            return () -> { };
        };
    }

    private static BeaconDeviceBenchmarkRunner certifiedNetworkOnlyDeviceRunner() {
        return (plan, observer) -> {
            assertTrue(plan.decoderRounds().isEmpty());
            observer.onCompleted(new BeaconBenchmarkDeviceEvidence(
                List.of(),
                List.of(new BeaconBenchmarkCompletionRequest.PowerSample(
                    100,
                    true,
                    "nominal"))));
            return () -> { };
        };
    }

    private static String decoderEvidence(BeaconBenchmarkDeviceEvidence evidence) {
        if (evidence == null) return "none";
        StringBuilder value = new StringBuilder("[");
        for (BeaconBenchmarkCompletionRequest.DecoderSample sample : evidence.decoderSamples()) {
            if (value.length() > 1) value.append(',');
            value.append(sample.toJson());
        }
        return value.append(']').toString();
    }

    private static final class Gate4BenchmarkOutcome {
        private final BeaconApiClient.BeaconResult completion;
        private final BeaconStreamCore.BenchmarkNetworkResult networkEvidence;
        private final BeaconBenchmarkDeviceEvidence deviceEvidence;

        Gate4BenchmarkOutcome(
            BeaconApiClient.BeaconResult completion,
            BeaconStreamCore.BenchmarkNetworkResult networkEvidence,
            BeaconBenchmarkDeviceEvidence deviceEvidence) {
            this.completion = completion;
            this.networkEvidence = networkEvidence;
            this.deviceEvidence = deviceEvidence;
        }
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
        AtomicReference<BeaconStreamCore> coreReference = new AtomicReference<>();
        Gate3VideoRuntime videoRuntime = new Gate3VideoRuntime(evidence);
        BeaconApiClient api = new BeaconApiClient(
            instrumentation.getTargetContext(), productionClientConfig(arguments, serverUrl, clientId));
        BeaconViewModel model = new BeaconViewModel(
            clientId,
            serverUrl,
            api,
            gate3StreamCoreFactory(evidence, coreReference),
            videoRuntime::createSession);
        try {
            registerAndLaunch(model);
            evidence.recordGrant(model.latestStream());
            evidence.awaitRenderedFrameFeedback();
            emit("BEACON_GATE3_WORKER_CRASH_ARMED");
            BeaconStreamCore core = coreReference.get();
            assertNotNull(core);
            core.awaitConnectionLossForTest(1);
            assertEquals(1, core.connectionLossCountForTest());
            assertTrue(core.stoppedForTest());
        } finally {
            try {
                model.close();
                BeaconStreamCore.awaitNativeRegistryIdleForTest();
                evidence.recordTransportClosedAfterNativeDrain();
            } finally {
                videoRuntime.close();
            }
        }

        assertTrue(evidence.transportConnected());
        assertTrue(evidence.receivedFrameCount() >= 1L);
        assertTrue(evidence.feedbackSent());
        assertTrue(evidence.surfacePresented());
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

    private static BeaconClientConfig productionClientConfig(
        Bundle arguments,
        String serverUrl,
        String clientId) {
        return new BeaconClientConfig(
            serverUrl,
            clientId,
            requireArgument(arguments, "serverPublicKeyFingerprint"));
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
        registerAndLaunch(model, gate3Game());
    }

    private static void registerAndLaunch(
        BeaconViewModel model,
        BeaconApiClient.GameSelection game) throws Exception {
        model.refresh();
        assertSuccessful(model);
        model.beacon(true);
        assertSuccessful(model);
        model.reportCapabilities(gate3Capabilities());
        assertSuccessful(model);
        model.reportTelemetry(gate3Telemetry());
        assertSuccessful(model);
        model.requestPlan(game);
        assertSuccessful(model);
        model.launch(game);
        assertSuccessful(model);
    }

    private static BeaconViewModel.StreamCoreFactory gate3StreamCoreFactory(
        Gate3SessionEvidence evidence,
        AtomicReference<BeaconStreamCore> coreReference) {
        return (sink, audioSink, failureObserver, benchmarkObserver) -> {
            BeaconStreamCore core = new BeaconStreamCore(
                sink,
                audioSink,
                () -> { },
                stage -> {
                    evidence.recordStreamFailure(stage);
                    failureObserver.onFailure(stage);
                },
                benchmarkObserver);
            coreReference.set(core);
            return core;
        };
    }

    private static BeaconViewModel.StreamCoreFactory gate5StreamCoreFactory(
        Gate3SessionEvidence evidence,
        Gate5VideoRuntime videoRuntime,
        AtomicReference<BeaconStreamCore> coreReference) {
        return (sink, audioSink, failureObserver, benchmarkObserver) -> {
            BeaconStreamCore core = new BeaconStreamCore(
                sink,
                audioSink,
                () -> { },
                stage -> {
                    evidence.recordStreamFailure(stage);
                    videoRuntime.recordStreamFailure(stage);
                    failureObserver.onFailure(stage);
                },
                benchmarkObserver);
            coreReference.set(core);
            return core;
        };
    }

    private static final class Gate3VideoRuntime implements AutoCloseable {
        private final Gate3SessionEvidence evidence;
        private final BenchmarkPresentationSurface presentationSurface;
        private final EncodedVideoSurfaceProvider surfaceProvider;

        Gate3VideoRuntime(Gate3SessionEvidence evidence) {
            this.evidence = evidence;
            presentationSurface = new AndroidImageReaderPresentationSurfaceFactory().create(
                1280,
                720,
                new BenchmarkPresentationSurfaceFactory.Observer() {
                    @Override
                    public void onFramePresented(
                        long presentationTimeUs,
                        long presentedAtNs) {
                        evidence.recordSurfacePresentation(
                            presentationTimeUs,
                            presentedAtNs);
                    }

                    @Override
                    public void onFailure(Throwable failure) {
                        evidence.recordVideoFailure(failure);
                    }
                });
            surfaceProvider = presentationSurface::surface;
        }

        BeaconViewModel.VideoSession createSession(
            BeaconVideoFeedbackBridge.FailureObserver failureObserver) {
            return new BeaconVideoSession(
                surfaceProvider,
                failure -> {
                    evidence.recordVideoFailure(failure);
                    failureObserver.onFailure(failure);
                },
                evidence);
        }

        @Override
        public void close() {
            presentationSurface.close();
        }
    }

    private static final class Gate5VideoRuntime implements AutoCloseable {
        private final Instrumentation instrumentation;
        private final Gate3SessionEvidence sessionEvidence;
        private final BeaconActivity activity;
        private final AndroidSurfaceViewProvider surfaceProvider;
        private final Gate5SurfaceEvidence surfaceEvidence;

        Gate5VideoRuntime(
            Instrumentation instrumentation,
            Gate3SessionEvidence sessionEvidence) {
            this.instrumentation = instrumentation;
            this.sessionEvidence = sessionEvidence;
            Intent intent = new Intent(
                instrumentation.getTargetContext(), BeaconActivity.class);
            intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
            activity = (BeaconActivity) instrumentation.startActivitySync(intent);
            instrumentation.waitForIdleSync();
            AtomicReference<SurfaceView> surface = new AtomicReference<>();
            AtomicReference<AndroidSurfaceViewProvider> provider = new AtomicReference<>();
            instrumentation.runOnMainSync(() -> {
                surface.set(activity.videoSurfaceViewForInstrumentation());
                provider.set(activity.videoSurfaceProviderForInstrumentation());
            });
            if (surface.get() == null || provider.get() == null) {
                throw new IllegalStateException("Beacon Gate 5 SurfaceView is unavailable.");
            }
            surfaceProvider = provider.get();
            surfaceEvidence = new Gate5SurfaceEvidence(surface.get(), sessionEvidence);
        }

        BeaconViewModel.VideoSession createSession(
            BeaconVideoFeedbackBridge.FailureObserver failureObserver) {
            return new BeaconVideoSession(
                surfaceProvider,
                failure -> {
                    surfaceEvidence.recordFailure(failure);
                    sessionEvidence.recordVideoFailure(failure);
                    failureObserver.onFailure(failure);
                },
                surfaceEvidence);
        }

        void awaitChangingFrames() throws InterruptedException {
            surfaceEvidence.awaitChangingFrames();
        }

        int frameCount() {
            return surfaceEvidence.frameCount();
        }

        long pixelVariantCount() {
            return surfaceEvidence.pixelVariantCount();
        }

        void recordStreamFailure(String stage) {
            surfaceEvidence.recordFailure(new AssertionError(
                "Gate 5 stream failed before Surface evidence completed: " + stage));
        }

        @Override
        public void close() {
            surfaceEvidence.close();
            CountDownLatch destroyed = new CountDownLatch(1);
            ActivityLifecycleMonitor monitor = ActivityLifecycleMonitorRegistry.getInstance();
            ActivityLifecycleCallback callback = (candidate, stage) -> {
                if (candidate == activity && stage == Stage.DESTROYED) {
                    destroyed.countDown();
                }
            };
            monitor.addLifecycleCallback(callback);
            instrumentation.runOnMainSync(() -> {
                if (activity.isDestroyed()) {
                    destroyed.countDown();
                } else {
                    activity.finish();
                }
            });
            try {
                destroyed.await();
            } catch (InterruptedException interrupted) {
                Thread.currentThread().interrupt();
                throw new IllegalStateException(
                    "Interrupted while closing the Beacon Gate 5 activity.", interrupted);
            } finally {
                monitor.removeLifecycleCallback(callback);
            }
        }
    }

    private static final class Gate5AudioRuntime {
        private final AtomicReference<Throwable> failure = new AtomicReference<>();
        private final AtomicInteger decodedFrames = new AtomicInteger();
        private final AtomicInteger writtenFrames = new AtomicInteger();
        private final AtomicLong lastSequence = new AtomicLong();

        BeaconViewModel.AudioSession createSession(
            BeaconAudioSession.FailureObserver failureObserver) {
            ExecutorService executor = Executors.newSingleThreadExecutor(
                action -> new Thread(action, "beacon-gate5-audio"));
            BeaconAudioSession delegate = new BeaconAudioSession(
                executor,
                (sampleRateHz, channelCount) -> new RecordingAudioOutput(
                    new AndroidAudioTrackOutput(sampleRateHz, channelCount)),
                observed -> {
                    failure.compareAndSet(null, observed);
                    failureObserver.onFailure(observed);
                });
            return new BeaconViewModel.AudioSession() {
                @Override
                public void start(
                    long generation,
                    BeaconStreamSession.SelectedAudio audio) {
                    delegate.start(generation, audio);
                }

                @Override
                public void onAudioPcm(BeaconStreamCore.DecodedAudioFrame frame) {
                    long previous = lastSequence.getAndSet(frame.sequence);
                    if (previous != 0 && frame.sequence <= previous) {
                        failure.compareAndSet(
                            null,
                            new AssertionError(
                                "Beacon audio sequence did not increase: "
                                    + previous + " -> " + frame.sequence));
                    }
                    decodedFrames.incrementAndGet();
                    delegate.onAudioPcm(frame);
                }

                @Override public void stop() { delegate.stop(); }

                @Override public void close() { delegate.close(); }
            };
        }

        void assertPlayback() {
            Throwable observed = failure.get();
            if (observed != null) {
                throw new AssertionError("Beacon production audio failed.", observed);
            }
            assertTrue("No ordered Opus PCM reached the Android client.",
                decodedFrames.get() > 0 && lastSequence.get() > 0);
            assertTrue("No PCM frame was written to the Android audio device.",
                writtenFrames.get() > 0);
        }

        int writtenFrameCount() {
            return writtenFrames.get();
        }

        private final class RecordingAudioOutput
            implements BeaconAudioSession.AudioOutput {
            private final AndroidAudioTrackOutput delegate;

            RecordingAudioOutput(AndroidAudioTrackOutput delegate) {
                this.delegate = delegate;
            }

            @Override public void play() { delegate.play(); }

            @Override
            public void write(ByteBuffer pcm) {
                delegate.write(pcm);
                writtenFrames.incrementAndGet();
            }

            @Override public void stop() { delegate.stop(); }

            @Override public void release() { delegate.release(); }
        }
    }

    private static void assertFirstInvocationEvidence(Gate3SessionEvidence evidence) {
        assertTrue(evidence.transportConnected());
        assertTrue(evidence.receivedFrameCount() >= 1L);
        assertTrue(evidence.frameSequence() > 0L);
        assertTrue(evidence.inputSent());
        assertTrue(evidence.feedbackSent());
        assertTrue(evidence.surfacePresented());
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

    private static BeaconBenchmarkPrepareRequest gate4BenchmarkRequest(
        Context context,
        String serverUrl,
        String trigger) {
        return new BeaconBenchmarkPrepareRequest(
            trigger,
            new BeaconBenchmarkPrepareRequest.FingerprintSet(
                BeaconBenchmarkPrepareRequest.NetworkFingerprint.fromLocalNetwork(
                    3,
                    serverUrl,
                    "wifi",
                    "10.0.2.0/24",
                    "emulator",
                    null,
                    "emulator",
                    null,
                    null,
                    BeaconNetworkIdentityHasher.system(context)),
                new BeaconBenchmarkPrepareRequest.HardwareFingerprint(
                    3,
                    "emulator-h264-v1",
                    "15",
                    "0.1.0",
                    "1280x720@60",
                    "h264-high-8-v1")));
    }

    private static BeaconStreamSession session(String sessionId) {
        return BeaconStreamSession.parse(
            "https://beacon.example",
            "client",
            "{\"connection\":{\"protocolVersion\":1,\"ticket\":\"AQID\",\"expiresAt\":\"2030-01-01T00:00:00Z\",\"planRevision\":1,\"planExplanation\":\"selected\",\"sessionId\":\"" + sessionId + "\",\"port\":47990,\"publicKeyFingerprint\":\"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA\",\"selectedVideo\":{\"codec\":\"h264\",\"width\":1280,\"height\":720,\"framesPerSecondNumerator\":60,\"framesPerSecondDenominator\":1,\"dynamicRange\":\"sdr\"}}}");
    }

    private static ByteBuffer directBuffer(int... values) {
        ByteBuffer buffer = ByteBuffer.allocateDirect(values.length);
        for (int value : values) buffer.put((byte) value);
        buffer.flip();
        return buffer;
    }

    private static final class RecordingBindings implements BeaconStreamCore.Bindings {
        BeaconStreamCore.NativeCallbacks callbacks;
        int stopCount;
        int releaseCount;
        final CountDownLatch stopEntered = new CountDownLatch(1);

        @Override public long create(BeaconStreamCore.NativeCallbacks value) { callbacks = value; return 11; }
        @Override public boolean start(long handle, BeaconStreamSession.NativeGrant grant) {
            return true;
        }
        @Override public void sendInput(long handle, BeaconApiClient.InputBatch input) { }
        @Override public void replaceSurface(long handle, Object surface) { }
        @Override public void stop(long handle) {
            stopCount++;
            stopEntered.countDown();
        }
        @Override public void release(long handle) { releaseCount++; }
    }
}
