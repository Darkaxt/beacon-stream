package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Intent;
import android.os.Bundle;
import android.util.Log;
import android.view.SurfaceView;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.runner.lifecycle.ActivityLifecycleCallback;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitor;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry;
import androidx.test.runner.lifecycle.Stage;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.util.List;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;
import static org.junit.Assume.assumeTrue;

@RunWith(AndroidJUnit4.class)
public final class HostedEmulatorStreamInstrumentationTest {
    private static final String Ticket =
        "YmVhY29uLWhvc3RlZC1lbXVsYXRvci10aWNrZXQtdjE=";

    @Test
    public void rendersChangingH264FramesThroughProductionStreamCore() throws Exception {
        Bundle arguments = InstrumentationRegistry.getArguments();
        String portValue = arguments.getString("hostedStreamEndpointPort");
        String fingerprint = arguments.getString("hostedStreamPublicKeyFingerprint");
        assumeTrue(
            "Hosted stream acceptance requires endpoint arguments.",
            portValue != null && !portValue.isBlank() &&
                fingerprint != null && !fingerprint.isBlank());

        int port = Integer.parseInt(portValue);
        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        BeaconActivity activity = launchActivity(instrumentation);
        CountDownLatch activityDestroyed = new CountDownLatch(1);
        ActivityLifecycleMonitor monitor = ActivityLifecycleMonitorRegistry.getInstance();
        ActivityLifecycleCallback lifecycleCallback = (candidate, stage) -> {
            if (candidate == activity && stage == Stage.DESTROYED) {
                activityDestroyed.countDown();
            }
        };
        monitor.addLifecycleCallback(lifecycleCallback);

        ExecutorService decoderExecutor = null;
        BeaconVideoFeedbackBridge feedback = null;
        BeaconVideoPipeline pipeline = null;
        BeaconStreamCore core = null;
        SurfaceFrameEvidence evidence = null;
        try {
            decoderExecutor = Executors.newSingleThreadExecutor(
                action -> new Thread(action, "beacon-hosted-video-decoder"));
            AtomicReference<SurfaceFrameEvidence> evidenceReference = new AtomicReference<>();
            feedback = new BeaconVideoFeedbackBridge(failure -> {
                SurfaceFrameEvidence activeEvidence = evidenceReference.get();
                if (activeEvidence != null) activeEvidence.onFailure(failure);
            });
            AtomicReference<SurfaceView> surfaceReference = new AtomicReference<>();
            AtomicReference<AndroidSurfaceViewProvider> providerReference =
                new AtomicReference<>();
            instrumentation.runOnMainSync(() -> {
                surfaceReference.set(activity.videoSurfaceViewForInstrumentation());
                providerReference.set(activity.videoSurfaceProviderForInstrumentation());
            });
            SurfaceView surfaceView = surfaceReference.get();
            assertNotNull(surfaceView);
            assertNotNull(providerReference.get());
            evidence = new SurfaceFrameEvidence(surfaceView, feedback, 30);
            evidenceReference.set(evidence);
            pipeline = new BeaconVideoPipeline(
                new AndroidMediaCodecFactory(),
                providerReference.get(),
                3,
                decoderExecutor,
                evidence);
            core = new BeaconStreamCore(
                pipeline,
                () -> { },
                stage -> evidenceReference.get().onFailure(
                    new IllegalStateException("StreamCore failed at " + stage + ".")));
            pipeline.start("h264", 640, 360, 30);
            long generation = core.start(session(port, fingerprint));
            feedback.activate(generation, new CoreFeedbackSink(core));
            evidence.awaitCompletion();
            core.stop();
        } finally {
            if (feedback != null) feedback.deactivate();
            try {
                if (core != null) core.close();
            } finally {
                try {
                    if (pipeline != null) pipeline.close();
                } finally {
                    if (decoderExecutor != null) decoderExecutor.shutdown();
                    BeaconStreamCore.awaitNativeRegistryIdleForTest();
                    instrumentation.runOnMainSync(activity::finish);
                    try {
                        activityDestroyed.await();
                    } finally {
                        monitor.removeLifecycleCallback(lifecycleCallback);
                    }
                }
            }
        }

        assertNotNull(evidence);
        assertNotNull(core);
        assertNotNull(decoderExecutor);
        List<Long> sequences = evidence.renderedSequences();
        List<Long> checksums = evidence.pixelChecksums();
        assertEquals(30, sequences.size());
        assertEquals(30, checksums.size());
        assertTrue(checksums.stream().allMatch(value -> value != 0));
        assertTrue(checksums.stream().distinct().count() >= 2);
        assertTrue(core.callbackExecutorShutdown());
        assertTrue(decoderExecutor.isShutdown());
        assertTrue(activity.isDestroyed());
        emit("BEACON_HOSTED_STREAM_FRAMES " + sequences.size());
        emit("BEACON_HOSTED_STREAM_PIXEL_VARIANTS " +
            checksums.stream().distinct().count());
    }

    private static BeaconActivity launchActivity(Instrumentation instrumentation) {
        Intent intent = new Intent(
            instrumentation.getTargetContext(),
            BeaconActivity.class);
        intent.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK);
        BeaconActivity activity =
            (BeaconActivity) instrumentation.startActivitySync(intent);
        instrumentation.waitForIdleSync();
        return activity;
    }

    private static BeaconStreamSession session(int port, String fingerprint) {
        return BeaconStreamSession.parse(
            "https://127.0.0.1",
            "hosted-emulator",
            "{\"connection\":{" +
                "\"protocolVersion\":1," +
                "\"ticket\":\"" + Ticket + "\"," +
                "\"expiresAt\":\"2099-01-01T00:00:00Z\"," +
                "\"planRevision\":1," +
                "\"planExplanation\":\"hosted-emulator-acceptance\"," +
                "\"sessionId\":\"hosted-emulator-stream\"," +
                "\"port\":" + port + "," +
                "\"publicKeyFingerprint\":\"" + fingerprint + "\"," +
                "\"selectedVideo\":{" +
                    "\"codec\":\"h264\"," +
                    "\"width\":640," +
                    "\"height\":360," +
                    "\"framesPerSecondNumerator\":30," +
                    "\"framesPerSecondDenominator\":1," +
                    "\"dynamicRange\":\"sdr\"}," +
                "\"selectedAudio\":{" +
                    "\"codec\":\"opus\"," +
                    "\"sampleRateHz\":48000," +
                    "\"channelCount\":2," +
                    "\"frameDurationUs\":20000," +
                    "\"bitrateBps\":96000}}}");
    }

    private static void emit(String marker) {
        Log.i("BeaconHostedStream", marker);
        System.out.println(marker);
    }

    private static final class CoreFeedbackSink
        implements BeaconVideoFeedbackBridge.Sink {
        private final BeaconStreamCore core;

        CoreFeedbackSink(BeaconStreamCore core) {
            this.core = core;
        }

        @Override
        public void sendQueueDepth(
            long generation,
            int queuedAccessUnits,
            long droppedAccessUnits) {
            core.sendQueueDepthFeedback(
                generation, queuedAccessUnits, droppedAccessUnits);
        }

        @Override
        public void sendDecoderState(
            long generation,
            BeaconStreamCore.DecoderState state,
            int platformErrorCode) {
            core.sendDecoderFeedback(generation, state, platformErrorCode);
        }

        @Override
        public void sendRenderedFrame(
            long generation,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            core.sendRenderedFrameFeedback(
                generation, frameSequence, presentationTimeUs, renderedAtUs);
        }

        @Override
        public void requestDecoderIdr(
            long generation,
            long lastCompleteSequence) {
            core.requestDecoderIdr(generation, lastCompleteSequence);
        }
    }
}
