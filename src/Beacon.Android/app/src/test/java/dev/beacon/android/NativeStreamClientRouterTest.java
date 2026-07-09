package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class NativeStreamClientRouterTest {
    @Test
    public void delegatesToFirstSupportingProtocolClient() {
        RecordingProtocolClient first = new RecordingProtocolClient(true, NativeStreamStartResult.started("first"));
        RecordingProtocolClient second = new RecordingProtocolClient(true, NativeStreamStartResult.started("second"));
        NativeStreamClientRouter router = new NativeStreamClientRouter(first, second);

        NativeStreamStartResult result = router.start(connection("beacon-test"));

        assertTrue(result.success());
        assertEquals("first", result.status());
        assertEquals(1, first.startCount);
        assertEquals(0, second.startCount);
    }

    @Test
    public void returnsConnectionDiagnosticWhenNoProtocolClientSupportsConnection() {
        RecordingProtocolClient client = new RecordingProtocolClient(false, NativeStreamStartResult.started("ignored"));
        NativeStreamClientRouter router = new NativeStreamClientRouter(client);

        NativeStreamStartResult result = router.start(connection("unknown"));

        assertFalse(result.success());
        assertEquals("", result.status());
        assertEquals(
            "Stream connection did not include a launch URI. protocol=unknown endpoints=none",
            result.diagnostic());
        assertEquals(0, client.startCount);
    }

    @Test
    public void absentConnectionIsIgnored() {
        RecordingProtocolClient client = new RecordingProtocolClient(true, NativeStreamStartResult.started("ignored"));
        NativeStreamClientRouter router = new NativeStreamClientRouter(client);

        NativeStreamStartResult result = router.start(null);

        assertFalse(result.success());
        assertEquals("", result.status());
        assertEquals("", result.diagnostic());
        assertEquals(0, client.startCount);
    }

    @Test
    public void stopDelegatesOnlyToActiveSuccessfulProtocolClient() {
        RecordingProtocolClient success = new RecordingProtocolClient(true, NativeStreamStartResult.started("running"));
        NativeStreamClientRouter router = new NativeStreamClientRouter(success);
        router.start(connection("beacon-test"));

        router.stop();

        assertEquals(1, success.stopCount);

        RecordingProtocolClient unsupported = new RecordingProtocolClient(true, NativeStreamStartResult.unsupported("no"));
        NativeStreamClientRouter unsupportedRouter = new NativeStreamClientRouter(unsupported);
        unsupportedRouter.start(connection("beacon-test"));

        unsupportedRouter.stop();

        assertEquals(0, unsupported.stopCount);
    }

    private static StreamConnectionDescriptor connection(String protocol) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"" + protocol + "\"}}}");
    }

    private static final class RecordingProtocolClient implements NativeStreamProtocolClient {
        private final boolean supports;
        private final NativeStreamStartResult result;
        int startCount;
        int stopCount;

        RecordingProtocolClient(boolean supports, NativeStreamStartResult result) {
            this.supports = supports;
            this.result = result;
        }

        @Override
        public boolean supports(StreamConnectionDescriptor connection) {
            return supports;
        }

        @Override
        public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
            startCount++;
            return result;
        }

        @Override
        public void stop() {
            stopCount++;
        }
    }
}
