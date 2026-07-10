package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightConnectionListener;
import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;
import dev.beacon.streaming.moonlight.MoonlightNativeStartResult;
import dev.beacon.streaming.moonlight.MoonlightVideoRenderer;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class MoonlightNativeStreamClientTest {
    @Test
    public void validNativeSessionStartsMoonlightAndSelectsEncodedVideoPresentation() {
        RecordingConnection connection = new RecordingConnection(MoonlightNativeStartResult.started());
        RecordingRenderer renderer = new RecordingRenderer();
        MoonlightNativeStreamClient client = new MoonlightNativeStreamClient(connection, () -> renderer);
        StreamConnectionDescriptor descriptor = validConnection();

        NativeStreamStartResult result = client.start(descriptor);

        assertTrue(result.success());
        assertEquals("encoded-video", result.presentation().kind());
        assertEquals("rtsp://10.0.2.2:48010/session/123", result.presentation().endpointUri());
        assertSame(descriptor.nativeSession(), connection.startedPlan);
        assertSame(renderer, connection.startedRenderer);
        assertEquals(1, connection.startCount);
    }

    @Test
    public void invalidProvidedNativeSessionReportsExactContractDiagnostic() {
        RecordingConnection connection = new RecordingConnection(MoonlightNativeStartResult.started());
        MoonlightNativeStreamClient client = new MoonlightNativeStreamClient(connection, RecordingRenderer::new);
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            response(validNativeSessionFields().replace(
                "\"remoteInputAesKey\":\"AAECAwQFBgcICQoLDA0ODw==\"",
                "\"remoteInputAesKey\":\"bad\"")));

        NativeStreamStartResult result = client.start(descriptor);

        assertFalse(result.success());
        assertEquals(descriptor.nativeSessionDiagnostic(), result.diagnostic());
        assertEquals(0, connection.startCount);
    }

    @Test
    public void failedNativeStartReleasesRendererAndReturnsNativeDiagnostic() {
        RecordingConnection connection = new RecordingConnection(
            MoonlightNativeStartResult.failed(-5, "RTSP handshake failed."));
        RecordingRenderer renderer = new RecordingRenderer();
        MoonlightNativeStreamClient client = new MoonlightNativeStreamClient(connection, () -> renderer);

        NativeStreamStartResult result = client.start(validConnection());

        assertFalse(result.success());
        assertEquals("RTSP handshake failed.", result.diagnostic());
        assertEquals(1, renderer.cleanupCount);
    }

    @Test
    public void stopReleasesSuccessfulNativeConnectionAndRenderer() {
        RecordingConnection connection = new RecordingConnection(MoonlightNativeStartResult.started());
        RecordingRenderer renderer = new RecordingRenderer();
        MoonlightNativeStreamClient client = new MoonlightNativeStreamClient(connection, () -> renderer);

        client.start(validConnection());
        client.stop();

        assertEquals(1, connection.stopCount);
        assertEquals(1, renderer.cleanupCount);
    }

    @Test
    public void supportsOnlyConnectionsThatSupplyNativeSessionContract() {
        MoonlightNativeStreamClient client = new MoonlightNativeStreamClient(
            new RecordingConnection(MoonlightNativeStartResult.started()),
            RecordingRenderer::new);

        assertTrue(client.supports(validConnection()));
        assertTrue(client.supports(StreamConnectionDescriptor.extract(response("\"width\":1"))));
        assertFalse(client.supports(StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\"}}}")));
    }

    static StreamConnectionDescriptor validConnection() {
        return StreamConnectionDescriptor.extract(response(validNativeSessionFields()));
    }

    private static String response(String nativeSessionFields) {
        return "{" +
            "\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"launchUri\":\"moonlight://beacon\"}}," +
            "\"nativeSession\":{" + nativeSessionFields + "}}";
    }

    private static String validNativeSessionFields() {
        return "\"address\":\"10.0.2.2\"," +
            "\"serverAppVersion\":\"7.1.431.0\"," +
            "\"serverGfeVersion\":\"3.27.0.120\"," +
            "\"rtspSessionUrl\":\"rtsp://10.0.2.2:48010/session/123\"," +
            "\"serverCodecModeSupport\":769," +
            "\"width\":2560," +
            "\"height\":1600," +
            "\"fps\":120," +
            "\"bitrateKbps\":45000," +
            "\"packetSize\":1024," +
            "\"streamingMode\":\"local\"," +
            "\"audioConfiguration\":\"stereo\"," +
            "\"videoFormat\":\"hevc-main10\"," +
            "\"clientRefreshRateX100\":12000," +
            "\"colorSpace\":\"rec2020\"," +
            "\"colorRange\":\"full\"," +
            "\"encryptionMode\":\"all\"," +
            "\"remoteInputAesKey\":\"AAECAwQFBgcICQoLDA0ODw==\"," +
            "\"remoteInputAesIv\":\"EBESExQVFhcYGRobHB0eHw==\"";
    }

    private static final class RecordingConnection implements MoonlightStreamConnection {
        private final MoonlightNativeStartResult result;
        private MoonlightNativeSessionPlan startedPlan;
        private MoonlightVideoRenderer startedRenderer;
        private int startCount;
        private int stopCount;

        private RecordingConnection(MoonlightNativeStartResult result) {
            this.result = result;
        }

        @Override
        public MoonlightNativeStartResult start(
            MoonlightNativeSessionPlan plan,
            MoonlightVideoRenderer renderer,
            MoonlightConnectionListener listener) {
            startCount++;
            startedPlan = plan;
            startedRenderer = renderer;
            return result;
        }

        @Override
        public void stop() {
            stopCount++;
        }
    }

    private static final class RecordingRenderer implements MoonlightVideoRenderer {
        private int cleanupCount;

        @Override
        public int capabilities() {
            return 0;
        }

        @Override
        public int setup(int videoFormat, int width, int height, int fps) {
            return DR_OK;
        }

        @Override
        public void start() {
        }

        @Override
        public void stop() {
        }

        @Override
        public void cleanup() {
            cleanupCount++;
        }

        @Override
        public int submitDecodeUnit(
            byte[] data,
            int length,
            int bufferType,
            int frameType,
            int frameNumber,
            long presentationTimeUs) {
            return DR_OK;
        }
    }
}
