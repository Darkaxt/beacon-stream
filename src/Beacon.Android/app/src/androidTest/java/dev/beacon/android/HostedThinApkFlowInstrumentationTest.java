package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Context;
import android.content.Intent;
import android.graphics.Bitmap;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.view.PixelCopy;
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
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.util.function.Predicate;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertTrue;
import static org.junit.Assume.assumeTrue;

@RunWith(AndroidJUnit4.class)
public final class HostedThinApkFlowInstrumentationTest {
    private static final String CredentialEvidenceFile = "beacon-gate3-client-credential";
    private static final String HostedGameId = "steam-shortcut:3767414131";
    private static final int EvidenceFrames = 12;

    @Test
    public void coldLaunchDrivesEntireProductionActivityTransaction() throws Exception {
        Bundle arguments = InstrumentationRegistry.getArguments();
        String serverUrl = arguments.getString("serverUrl");
        String clientId = arguments.getString("clientId");
        String fingerprint = arguments.getString("serverPublicKeyFingerprint");
        assumeTrue(
            "Hosted thin-APK acceptance requires TestHost arguments.",
            present(serverUrl) && present(clientId) && present(fingerprint));

        Instrumentation instrumentation = InstrumentationRegistry.getInstrumentation();
        Context context = instrumentation.getTargetContext();
        BeaconClientConfig enrollment = new BeaconClientConfig(
            serverUrl,
            clientId,
            fingerprint);
        installCredential(context, clientId);
        new BeaconEnrollmentStore(new SharedPreferencesEnrollmentStorage(
            context.getSharedPreferences("beacon", Context.MODE_PRIVATE)))
            .save(enrollment);
        emit("BEACON_HOSTED_THIN_ENROLLMENT_SEEDED");

        BeaconActivity activity = launchActivity(instrumentation);
        ActivityLifecycleMonitor monitor = ActivityLifecycleMonitorRegistry.getInstance();
        Object destroyedGate = new Object();
        boolean[] destroyed = { false };
        ActivityLifecycleCallback lifecycle = (candidate, stage) -> {
            if (candidate == activity && stage == Stage.DESTROYED) {
                synchronized (destroyedGate) {
                    destroyed[0] = true;
                    destroyedGate.notifyAll();
                }
            }
        };
        monitor.addLifecycleCallback(lifecycle);

        ActivityEventProbe events = new ActivityEventProbe();
        HostedActivitySurfaceEvidence frameEvidence = null;
        try {
            activity.activityEventsForInstrumentation().addObserver(events);
            BeaconActivityEventSource.Event presence = events.await(event ->
                event.kind() == BeaconActivityEventSource.Kind.PRESENCE_ACTIVE &&
                    event.operation().equals("Automatic connect"));
            assertTrue(presence.detail(), presence.detail().contains("beacon active"));
            emit("BEACON_HOSTED_THIN_AUTOMATIC_PRESENCE_OK");

            BeaconActivityEventSource.Event automaticBenchmark = events.await(event ->
                event.kind() == BeaconActivityEventSource.Kind.BENCHMARK_COMPLETE &&
                    event.operation().equals("automatic"));
            assertTrue(automaticBenchmark.detail(), automaticBenchmark.detail().startsWith("status=2"));
            emit("BEACON_HOSTED_THIN_AUTOMATIC_BENCHMARK_OK");

            runOnMain(instrumentation, activity::loadGamesForInstrumentation);
            BeaconActivityEventSource.Event catalog = events.await(event ->
                event.kind() == BeaconActivityEventSource.Kind.CATALOG_LOADED &&
                    event.operation().equals("Load Games"));
            assertEquals(HostedGameId, catalog.detail());
            events.await(actionComplete("Load Games"));
            emit("BEACON_HOSTED_THIN_CATALOG_OK " + HostedGameId);

            SurfaceView surface = readSurface(instrumentation, activity);
            frameEvidence = new HostedActivitySurfaceEvidence(surface, EvidenceFrames);
            HostedActivitySurfaceEvidence installedEvidence = frameEvidence;
            runOnMain(instrumentation, () ->
                activity.installVideoObserverForInstrumentation(
                    installedEvidence::decorate));

            runOnMain(instrumentation, activity::launchForInstrumentation);
            BeaconActivityEventSource.Event preflightBenchmark = events.await(event ->
                event.kind() == BeaconActivityEventSource.Kind.BENCHMARK_COMPLETE &&
                    event.operation().equals("sessionPreflight"));
            assertEquals("accepted", preflightBenchmark.detail());
            BeaconActivityEventSource.Event launchGrant = events.await(connectionGrant("Launch"));
            events.await(actionComplete("Launch"));
            frameEvidence.awaitPhase(0);
            assertChangingFrames(frameEvidence.checksums(0));
            long launchGeneration = frameGeneration(events.snapshot());
            assertTrue(launchGeneration > 0);
            emit("BEACON_HOSTED_THIN_LAUNCH_RENDER_OK " + EvidenceFrames);

            runOnMain(instrumentation, activity::disconnectForInstrumentation);
            events.await(actionComplete("Disconnect"));
            assertFalse(readActiveStream(instrumentation, activity));
            emit("BEACON_HOSTED_THIN_DISCONNECT_RETAINED_OK");

            frameEvidence.beginReconnect();
            runOnMain(instrumentation, activity::reconnectForInstrumentation);
            BeaconActivityEventSource.Event reconnectGrant = events.await(connectionGrant("Reconnect"));
            events.await(actionComplete("Reconnect"));
            frameEvidence.awaitPhase(1);
            assertChangingFrames(frameEvidence.checksums(1));
            long reconnectGeneration = frameGeneration(events.snapshot());
            assertNotEquals(launchGeneration, reconnectGeneration);
            assertSameSessionFreshTicket(launchGrant.detail(), reconnectGrant.detail());
            emit("BEACON_HOSTED_THIN_RECONNECT_RENDER_OK " + EvidenceFrames);

            runOnMain(instrumentation, activity::quitForInstrumentation);
            events.await(event ->
                event.kind() == BeaconActivityEventSource.Kind.PRESENCE_DEPARTED &&
                    event.operation().equals("Quit"));
            events.await(actionComplete("Quit"));
            assertFalse(readActiveStream(instrumentation, activity));
            emit("BEACON_HOSTED_THIN_QUIT_OK");

            runOnMain(instrumentation, activity::emergencyRestoreForInstrumentation);
            events.await(actionComplete("Emergency Restore"));
            assertEquals(1, events.count(BeaconActivityEventSource.Kind.PRESENCE_ACTIVE));
            emit("BEACON_HOSTED_THIN_EMERGENCY_RESTORE_OK");
        } finally {
            activity.activityEventsForInstrumentation().removeObserver(events);
            runOnMain(instrumentation, activity::finish);
            synchronized (destroyedGate) {
                while (!destroyed[0]) destroyedGate.wait();
            }
            monitor.removeLifecycleCallback(lifecycle);
            activity.awaitWorkerCleanupForInstrumentation();
            BeaconStreamCore.awaitNativeRegistryIdleForTest();
        }

        assertNotNull(frameEvidence);
        assertTrue(activity.isDestroyed());
        assertTrue(activity.workerExecutorShutdown());
        emit("BEACON_HOSTED_THIN_ACTIVITY_CLEANUP_OK");
    }

    private static Predicate<BeaconActivityEventSource.Event> actionComplete(String operation) {
        return event -> event.kind() == BeaconActivityEventSource.Kind.ACTION_COMPLETE &&
            event.operation().equals(operation);
    }

    private static Predicate<BeaconActivityEventSource.Event> connectionGrant(String operation) {
        return event -> event.kind() == BeaconActivityEventSource.Kind.CONNECTION_GRANTED &&
            event.operation().equals(operation);
    }

    private static void assertChangingFrames(List<Long> checksums) {
        assertEquals(EvidenceFrames, checksums.size());
        assertTrue(checksums.toString(), checksums.stream().allMatch(value -> value != 0));
        assertTrue(checksums.toString(), checksums.stream().distinct().count() >= 2);
    }

    private static void assertSameSessionFreshTicket(String first, String second) {
        String firstSession = evidenceValue(first, "session");
        String secondSession = evidenceValue(second, "session");
        String firstTicket = evidenceValue(first, "ticketSha256");
        String secondTicket = evidenceValue(second, "ticketSha256");
        assertEquals(firstSession, secondSession);
        assertNotEquals(firstTicket, secondTicket);
        assertEquals(64, firstTicket.length());
        assertEquals(64, secondTicket.length());
    }

    private static String evidenceValue(String evidence, String name) {
        String prefix = name + "=";
        for (String item : evidence.split(" ")) {
            if (item.startsWith(prefix)) return item.substring(prefix.length());
        }
        throw new AssertionError("Missing " + name + " in sanitized grant evidence.");
    }

    private static long frameGeneration(List<BeaconActivityEventSource.Event> events) {
        long generation = 0;
        for (BeaconActivityEventSource.Event event : events) {
            if (event.kind() == BeaconActivityEventSource.Kind.VIDEO_FRAME) {
                generation = event.streamGeneration();
            }
        }
        return generation;
    }

    private static BeaconActivity launchActivity(Instrumentation instrumentation) {
        Intent intent = new Intent(instrumentation.getTargetContext(), BeaconActivity.class)
            .addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_CLEAR_TASK);
        BeaconActivity activity = (BeaconActivity) instrumentation.startActivitySync(intent);
        instrumentation.waitForIdleSync();
        return activity;
    }

    private static SurfaceView readSurface(
        Instrumentation instrumentation,
        BeaconActivity activity) {
        SurfaceView[] result = new SurfaceView[1];
        runOnMain(instrumentation, () -> result[0] = activity.videoSurfaceViewForInstrumentation());
        assertNotNull(result[0]);
        return result[0];
    }

    private static boolean readActiveStream(
        Instrumentation instrumentation,
        BeaconActivity activity) {
        boolean[] result = new boolean[1];
        runOnMain(instrumentation, () -> result[0] = activity.hasActiveStreamForInstrumentation());
        return result[0];
    }

    private static void runOnMain(Instrumentation instrumentation, Runnable action) {
        instrumentation.runOnMainSync(action);
    }

    private static void installCredential(Context context, String clientId) {
        AndroidKeyStoreCredentialStore store = new AndroidKeyStoreCredentialStore(context, clientId);
        File evidence = new File(context.getFilesDir(), CredentialEvidenceFile);
        long length = evidence.length();
        if (!evidence.isFile() || length <= 0 || length > 1024) {
            throw new AssertionError("Hosted client credential evidence is unavailable or invalid.");
        }
        byte[] bytes = new byte[(int) length];
        try (FileInputStream input = new FileInputStream(evidence)) {
            int offset = 0;
            while (offset < bytes.length) {
                int read = input.read(bytes, offset, bytes.length - offset);
                if (read < 0) throw new IOException("Unexpected end of credential evidence.");
                offset += read;
            }
            store.saveCredential(new String(bytes, StandardCharsets.UTF_8));
            assertNotNull(store.loadCredential());
        } catch (IOException error) {
            throw new AssertionError("Could not install hosted client credential.", error);
        } finally {
            Arrays.fill(bytes, (byte) 0);
            if (evidence.exists() && !evidence.delete()) {
                throw new AssertionError("Could not delete hosted client credential evidence.");
            }
        }
    }

    private static boolean present(String value) {
        return value != null && !value.isBlank();
    }

    private static void emit(String marker) {
        Log.i("BeaconHostedThinApk", marker);
        System.out.println(marker);
    }

    private static final class ActivityEventProbe implements BeaconActivityEventSource.Observer {
        private final List<BeaconActivityEventSource.Event> events = new ArrayList<>();

        @Override
        public synchronized void onEvent(BeaconActivityEventSource.Event event) {
            events.add(event);
            notifyAll();
        }

        synchronized BeaconActivityEventSource.Event await(
            Predicate<BeaconActivityEventSource.Event> predicate) throws InterruptedException {
            while (true) {
                for (BeaconActivityEventSource.Event event : events) {
                    if (predicate.test(event)) return event;
                    if (event.kind() == BeaconActivityEventSource.Kind.ACTION_FAILED) {
                        throw new AssertionError(
                            event.operation() + " failed: " + event.detail());
                    }
                }
                wait();
            }
        }

        synchronized int count(BeaconActivityEventSource.Kind kind) {
            return (int) events.stream().filter(event -> event.kind() == kind).count();
        }

        synchronized List<BeaconActivityEventSource.Event> snapshot() {
            return List.copyOf(events);
        }
    }

    private static final class HostedActivitySurfaceEvidence {
        private static final int Width = 64;
        private static final int Height = 36;

        private final Object gate = new Object();
        private final SurfaceView surface;
        private final int expectedFrames;
        private final Handler handler = new Handler(Looper.getMainLooper());
        private final List<List<Long>> checksums = List.of(new ArrayList<>(), new ArrayList<>());
        private int phase;
        private boolean phaseComplete;
        private boolean pixelCopyPending;
        private Throwable failure;

        HostedActivitySurfaceEvidence(SurfaceView surface, int expectedFrames) {
            this.surface = surface;
            this.expectedFrames = expectedFrames;
        }

        BeaconVideoPipeline.Observer decorate(BeaconVideoPipeline.Observer downstream) {
            return new BeaconVideoPipeline.Observer() {
                @Override
                public void onDecoderStateChanged(
                    BeaconVideoPipeline.DecoderState state,
                    int platformErrorCode) {
                    downstream.onDecoderStateChanged(state, platformErrorCode);
                }

                @Override
                public void onQueueDepthChanged(int queuedAccessUnits, long droppedAccessUnits) {
                    downstream.onQueueDepthChanged(queuedAccessUnits, droppedAccessUnits);
                }

                @Override
                public void onIdrRequired(long lastCompleteSequence) {
                    downstream.onIdrRequired(lastCompleteSequence);
                }

                @Override
                public void onFrameRendered(
                    long frameSequence,
                    long presentationTimeUs,
                    long renderedAtUs) {
                    capture(
                        downstream,
                        frameSequence,
                        presentationTimeUs,
                        renderedAtUs);
                }

                @Override
                public void onFailure(Throwable observed) {
                    recordFailure(observed);
                    downstream.onFailure(observed);
                }
            };
        }

        void beginReconnect() {
            synchronized (gate) {
                assertHealthy();
                if (!phaseComplete || phase != 0 || pixelCopyPending) {
                    throw new AssertionError("Launch frame evidence is incomplete.");
                }
                phase = 1;
                phaseComplete = false;
                gate.notifyAll();
            }
        }

        void awaitPhase(int expectedPhase) throws InterruptedException {
            synchronized (gate) {
                while (failure == null && (phase != expectedPhase || !phaseComplete)) gate.wait();
                assertHealthy();
            }
        }

        List<Long> checksums(int requestedPhase) {
            synchronized (gate) {
                return List.copyOf(checksums.get(requestedPhase));
            }
        }

        private void capture(
            BeaconVideoPipeline.Observer downstream,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            int capturedPhase;
            synchronized (gate) {
                if (failure != null || phaseComplete) return;
                if (pixelCopyPending) {
                    recordFailure(new IllegalStateException("PixelCopy evidence overlapped."));
                    return;
                }
                long expectedSequence = checksums.get(phase).size() + 1L;
                if (frameSequence != expectedSequence) {
                    recordFailure(new IllegalStateException(
                        "Expected frame " + expectedSequence + " but received " + frameSequence + "."));
                    return;
                }
                pixelCopyPending = true;
                capturedPhase = phase;
            }

            Bitmap bitmap = Bitmap.createBitmap(Width, Height, Bitmap.Config.ARGB_8888);
            try {
                PixelCopy.request(
                    surface,
                    bitmap,
                    result -> completeCopy(
                        downstream,
                        capturedPhase,
                        result,
                        bitmap,
                        frameSequence,
                        presentationTimeUs,
                        renderedAtUs),
                    handler);
            } catch (RuntimeException error) {
                bitmap.recycle();
                recordFailure(error);
            }
        }

        private void completeCopy(
            BeaconVideoPipeline.Observer downstream,
            int capturedPhase,
            int result,
            Bitmap bitmap,
            long frameSequence,
            long presentationTimeUs,
            long renderedAtUs) {
            if (result != PixelCopy.SUCCESS) {
                bitmap.recycle();
                recordFailure(new IllegalStateException("PixelCopy failed with result " + result + "."));
                return;
            }
            long checksum;
            try {
                checksum = checksum(bitmap);
            } finally {
                bitmap.recycle();
            }
            synchronized (gate) {
                pixelCopyPending = false;
                if (failure != null || phase != capturedPhase || phaseComplete) return;
                checksums.get(phase).add(checksum);
                phaseComplete = checksums.get(phase).size() == expectedFrames;
                gate.notifyAll();
            }
            downstream.onFrameRendered(frameSequence, presentationTimeUs, renderedAtUs);
        }

        private void recordFailure(Throwable observed) {
            synchronized (gate) {
                if (failure == null) failure = observed;
                pixelCopyPending = false;
                gate.notifyAll();
            }
        }

        private void assertHealthy() {
            if (failure != null) {
                throw new AssertionError("Hosted Activity frame evidence failed.", failure);
            }
        }

        private static long checksum(Bitmap bitmap) {
            int[] pixels = new int[bitmap.getWidth() * bitmap.getHeight()];
            bitmap.getPixels(
                pixels,
                0,
                bitmap.getWidth(),
                0,
                0,
                bitmap.getWidth(),
                bitmap.getHeight());
            long hash = 0xcbf29ce484222325L;
            boolean nonblank = false;
            for (int pixel : pixels) {
                int rgb = pixel & 0x00ffffff;
                nonblank |= rgb != 0;
                hash ^= Integer.toUnsignedLong(rgb);
                hash *= 0x100000001b3L;
            }
            if (!nonblank) return 0;
            return hash == 0 ? 1 : hash;
        }
    }
}
