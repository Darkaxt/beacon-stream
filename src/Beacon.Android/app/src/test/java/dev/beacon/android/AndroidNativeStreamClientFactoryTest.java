package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class AndroidNativeStreamClientFactoryTest {
    @Test
    public void gameStreamRouteUsesInjectedRtspSessionClient() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(
            GameStreamRtspSessionResult.started("RTSP play started."));
        NativeStreamClient client = AndroidNativeStreamClientFactory.create(
            new RecordingEncodedVideoDecoder(EncodedVideoDecodeResult.failed("unused")),
            rtspClient);

        NativeStreamStartResult result = client.start(gameStreamConnection());

        assertTrue(result.success());
        assertEquals(
            "Native GameStream RTSP session started. protocol=gamestream rtsp=rtsp://127.0.0.1:48010/beacon/session",
            result.status());
        assertEquals("", result.diagnostic());
        assertEquals(1, rtspClient.startCount);
        assertEquals("rtsp://127.0.0.1:48010/beacon/session", rtspClient.startedPlan.rtspUri());
    }

    @Test
    public void stopReleasesGameStreamRoute() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(
            GameStreamRtspSessionResult.started("RTSP play started."));
        NativeStreamClient client = AndroidNativeStreamClientFactory.create(
            new RecordingEncodedVideoDecoder(EncodedVideoDecodeResult.failed("unused")),
            rtspClient);

        client.start(gameStreamConnection());
        client.stop();

        assertEquals(1, rtspClient.stopCount);
    }

    @Test
    public void beaconTestRouteStillUsesColorBarsClient() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(
            GameStreamRtspSessionResult.started("should not start"));
        NativeStreamClient client = AndroidNativeStreamClientFactory.create(
            new RecordingEncodedVideoDecoder(EncodedVideoDecodeResult.failed("unused")),
            rtspClient);

        NativeStreamStartResult result = client.start(StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"beacon-test\",\"endpoints\":[{\"role\":\"video\",\"uri\":\"beacon-test://pattern/color-bars\"}]}}}"));

        assertTrue(result.success());
        assertEquals("color-bars", result.presentation().kind());
        assertEquals(0, rtspClient.startCount);
    }

    @Test
    public void encodedVideoRouteRunsBeforeBeaconTestAndGameStreamRoutes() {
        RecordingEncodedVideoDecoder decoder = new RecordingEncodedVideoDecoder(
            EncodedVideoDecodeResult.started("decoder ready"));
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(
            GameStreamRtspSessionResult.started("should not start"));
        NativeStreamClient client = AndroidNativeStreamClientFactory.create(decoder, rtspClient);

        NativeStreamStartResult result = client.start(EncodedVideoNativeStreamClientTest.validConnection());

        assertTrue(result.success());
        assertEquals("encoded-video", result.presentation().kind());
        assertEquals(1, decoder.startCount);
        assertEquals(0, rtspClient.startCount);
    }

    private static StreamConnectionDescriptor gameStreamConnection() {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");
    }

    private static final class RecordingRtspSessionClient implements GameStreamRtspSessionClient {
        private final GameStreamRtspSessionResult result;
        private GameStreamEndpointPlan startedPlan;
        private int startCount;
        private int stopCount;

        private RecordingRtspSessionClient(GameStreamRtspSessionResult result) {
            this.result = result;
        }

        @Override
        public GameStreamRtspSessionResult start(GameStreamEndpointPlan plan) {
            startCount++;
            startedPlan = plan;
            return result;
        }

        @Override
        public void stop() {
            stopCount++;
        }
    }

    private static final class RecordingEncodedVideoDecoder implements EncodedVideoDecoder {
        private final EncodedVideoDecodeResult result;
        private int startCount;

        private RecordingEncodedVideoDecoder(EncodedVideoDecodeResult result) {
            this.result = result;
        }

        @Override
        public EncodedVideoDecodeResult start(EncodedVideoDecodeRequest request) {
            startCount++;
            return result;
        }

        @Override
        public void stop() {
        }
    }
}
