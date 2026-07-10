package dev.beacon.streaming.moonlight;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class MoonlightNativeConnectionTest {
    @Test
    public void startsAndStopsOneNativeSession() {
        RecordingBindings bindings = new RecordingBindings();
        MoonlightNativeConnection connection = new MoonlightNativeConnection(bindings);
        MoonlightNativeSessionPlan plan = createPlan();
        MoonlightVideoRenderer renderer = new NoOpVideoRenderer();
        MoonlightConnectionListener listener = new MoonlightConnectionListener() { };

        MoonlightNativeStartResult start = connection.start(plan, renderer, listener);
        MoonlightNativeStartResult duplicate = connection.start(plan, renderer, listener);

        assertTrue(start.success());
        assertEquals(0, start.errorCode());
        assertTrue(connection.active());
        assertSame(plan, bindings.plan);
        assertSame(renderer, bindings.renderer);
        assertSame(listener, bindings.listener);
        assertFalse(duplicate.success());
        assertTrue(duplicate.diagnostic().contains("already active"));

        connection.stop();

        assertEquals(1, bindings.stopCalls);
        assertFalse(connection.active());
    }

    @Test
    public void failedStartReturnsCoreErrorAndReleasesLifecycle() {
        RecordingBindings bindings = new RecordingBindings();
        bindings.startResult = -42;
        MoonlightNativeConnection connection = new MoonlightNativeConnection(bindings);

        MoonlightNativeStartResult result = connection.start(
            createPlan(),
            new NoOpVideoRenderer(),
            new MoonlightConnectionListener() { });

        assertFalse(result.success());
        assertEquals(-42, result.errorCode());
        assertTrue(result.diagnostic().contains("-42"));
        assertFalse(connection.active());

        bindings.startResult = 0;
        assertTrue(connection.start(
            createPlan(),
            new NoOpVideoRenderer(),
            new MoonlightConnectionListener() { }).success());
    }

    @Test
    public void stopDuringStartInterruptsWithoutPrematureSecondStart() {
        RecordingBindings bindings = new RecordingBindings();
        bindings.startResult = -99;
        MoonlightNativeConnection connection = new MoonlightNativeConnection(bindings);
        bindings.duringStart = connection::stop;

        MoonlightNativeStartResult interrupted = connection.start(
            createPlan(),
            new NoOpVideoRenderer(),
            new MoonlightConnectionListener() { });

        assertFalse(interrupted.success());
        assertEquals(1, bindings.interruptCalls);
        assertFalse(connection.active());
        bindings.startResult = 0;
        assertTrue(connection.start(
            createPlan(),
            new NoOpVideoRenderer(),
            new MoonlightConnectionListener() { }).success());
    }

    @Test
    public void unavailableNativeLibraryFailsBeforeCallingBindings() {
        RecordingBindings bindings = new RecordingBindings();
        bindings.available = false;
        MoonlightNativeConnection connection = new MoonlightNativeConnection(bindings);

        MoonlightNativeStartResult result = connection.start(
            createPlan(),
            new NoOpVideoRenderer(),
            new MoonlightConnectionListener() { });

        assertFalse(result.success());
        assertEquals(0, bindings.startCalls);
        assertEquals("native unavailable", result.diagnostic());
    }

    private static MoonlightNativeSessionPlan createPlan() {
        return MoonlightNativeSessionPlan.create(
            "10.0.2.2",
            "7.1.431.0",
            null,
            "rtsp://10.0.2.2:48010/session/123",
            0x0301,
            2560,
            1600,
            120,
            45000,
            1024,
            "local",
            "stereo",
            "hevc-main10",
            12000,
            "rec2020",
            "full",
            "all",
            "AAECAwQFBgcICQoLDA0ODw==",
            "EBESExQVFhcYGRobHB0eHw==");
    }

    private static final class RecordingBindings implements MoonlightNativeConnection.Bindings {
        private boolean available = true;
        private int startResult;
        private int startCalls;
        private int stopCalls;
        private int interruptCalls;
        private Runnable duringStart;
        private MoonlightNativeSessionPlan plan;
        private MoonlightVideoRenderer renderer;
        private MoonlightConnectionListener listener;

        @Override
        public boolean available() {
            return available;
        }

        @Override
        public String unavailableDiagnostic() {
            return "native unavailable";
        }

        @Override
        public int start(
            MoonlightNativeSessionPlan plan,
            MoonlightVideoRenderer renderer,
            MoonlightConnectionListener listener) {
            startCalls++;
            this.plan = plan;
            this.renderer = renderer;
            this.listener = listener;
            if (duringStart != null) {
                Runnable callback = duringStart;
                duringStart = null;
                callback.run();
            }
            return startResult;
        }

        @Override
        public void stop() {
            stopCalls++;
        }

        @Override
        public void interrupt() {
            interruptCalls++;
        }
    }

    private static final class NoOpVideoRenderer implements MoonlightVideoRenderer {
        @Override
        public int capabilities() {
            return 0;
        }

        @Override
        public int setup(int videoFormat, int width, int height, int fps) {
            return 0;
        }

        @Override
        public void start() {
        }

        @Override
        public void stop() {
        }

        @Override
        public void cleanup() {
        }

        @Override
        public int submitDecodeUnit(
            byte[] data,
            int length,
            int bufferType,
            int frameType,
            int frameNumber,
            long presentationTimeUs) {
            return MoonlightVideoRenderer.DR_OK;
        }
    }
}
