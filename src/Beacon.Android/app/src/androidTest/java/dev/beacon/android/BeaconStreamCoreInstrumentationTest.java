package dev.beacon.android;

import android.app.Instrumentation;
import android.content.Intent;
import android.graphics.SurfaceTexture;
import android.view.Surface;

import androidx.test.ext.junit.runners.AndroidJUnit4;
import androidx.test.platform.app.InstrumentationRegistry;
import androidx.test.runner.lifecycle.ActivityLifecycleCallback;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitor;
import androidx.test.runner.lifecycle.ActivityLifecycleMonitorRegistry;
import androidx.test.runner.lifecycle.Stage;

import org.junit.Test;
import org.junit.runner.RunWith;

import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicInteger;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotEquals;

import static org.junit.Assert.assertTrue;
import static org.junit.Assert.assertThrows;

@RunWith(AndroidJUnit4.class)
public final class BeaconStreamCoreInstrumentationTest {
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

        bindings.callbacks.onFrame(new byte[] { 1 }, 2, 1);
        delivered.await();
        assertEquals("beacon-device-frame", callbackThread.get());
        assertNotEquals(Thread.currentThread().getName(), callbackThread.get());
        core.close();
        bindings.callbacks.onFrame(new byte[] { 2 }, 3, 1);
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
        bindings.callbacks.onFrame(new byte[] { 1 }, 1, 1);
        sinkEntered.await();
        bindings.callbacks.onFrame(new byte[] { 2 }, 2, 1);
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
                byte[] bytes, long presentationTimeUs, long generation) {
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
