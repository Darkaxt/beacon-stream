package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class GameStreamNativeStreamClientTest {
    @Test
    public void rejectsIncompleteGameStreamEndpointMap() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "GameStream endpoint map is incomplete. Missing required endpoints: video, control, audio. protocol=gamestream endpoints=rtsp=rtsp://127.0.0.1:48010",
            result.diagnostic());
    }

    @Test
    public void reportsDefaultRtspTransportNotConfiguredForCompleteGameStreamEndpointMap() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient();
        StreamConnectionDescriptor connection = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "Native GameStream RTSP transport is not configured yet. protocol=gamestream rtsp=rtsp://127.0.0.1:48010",
            result.diagnostic());
    }

    @Test
    public void startsConfiguredRtspSessionForCompleteGameStreamEndpointMap() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(
            GameStreamRtspSessionResult.started("RTSP session started."));
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient);
        StreamConnectionDescriptor connection = completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session");

        NativeStreamStartResult result = client.start(connection);

        assertTrue(result.success());
        assertEquals(
            "Native GameStream RTSP session started. protocol=gamestream rtsp=rtsp://127.0.0.1:48010/beacon/session",
            result.status());
        assertEquals("rtsp://127.0.0.1:48010/beacon/session", rtspClient.startedPlan.rtspUri());
    }

    @Test
    public void reportsConfiguredRtspSessionFailureForCompleteGameStreamEndpointMap() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(
            new RecordingRtspSessionClient(GameStreamRtspSessionResult.failed("RTSP DESCRIBE failed with status 503.")));
        StreamConnectionDescriptor connection = completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals("RTSP DESCRIBE failed with status 503.", result.diagnostic());
    }

    @Test
    public void startsRealHandshakeClientForCompleteGameStreamEndpointMap() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(
            new GameStreamRtspHandshakeClient(new SuccessfulRtspTransport()));
        StreamConnectionDescriptor connection = completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session");

        NativeStreamStartResult result = client.start(connection);

        assertTrue(result.success());
        assertEquals(
            "Native GameStream RTSP session started. protocol=gamestream rtsp=rtsp://127.0.0.1:48010/beacon/session",
            result.status());
    }

    @Test
    public void rejectsCompleteGameStreamEndpointMapWhenRtspEndpointIsNotReady() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(
            new RecordingRtspSessionClient(GameStreamRtspSessionResult.started("Should not run.")));
        StreamConnectionDescriptor connection = completeGameStreamConnection("https://127.0.0.1:48010/beacon/session");

        NativeStreamStartResult result = client.start(connection);

        assertFalse(result.success());
        assertEquals(
            "GameStream RTSP endpoint must use rtsp://. rtsp=https://127.0.0.1:48010/beacon/session",
            result.diagnostic());
    }

    @Test
    public void supportsGameStreamAndMoonlightProtocols() {
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient();

        assertTrue(client.supports(connection("gamestream")));
        assertTrue(client.supports(connection("moonlight")));
        assertFalse(client.supports(connection("beacon-test")));
        assertFalse(client.supports(null));
    }

    private static StreamConnectionDescriptor connection(String protocol) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"" + protocol + "\"}}}");
    }

    private static StreamConnectionDescriptor completeGameStreamConnection(String rtspUri) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"" + rtspUri + "\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");
    }

    private static final class RecordingRtspSessionClient implements GameStreamRtspSessionClient {
        private final GameStreamRtspSessionResult result;
        private GameStreamEndpointPlan startedPlan;

        private RecordingRtspSessionClient(GameStreamRtspSessionResult result) {
            this.result = result;
        }

        @Override
        public GameStreamRtspSessionResult start(GameStreamEndpointPlan plan) {
            startedPlan = plan;
            return result;
        }
    }

    private static final class SuccessfulRtspTransport implements RtspTransport {
        private int cseq = 1;

        @Override
        public RtspResponse transact(RtspRequest request) {
            int current = cseq++;
            if (current <= 2) {
                return RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: " + current + "\r\n\r\n");
            }

            int serverPort = current == 3 ? 48000 : current == 4 ? 47998 : 47999;
            return RtspResponse.parse(
                "RTSP/1.0 200 OK\r\n" +
                    "CSeq: " + current + "\r\n" +
                    "Session: session-1;timeout=30\r\n" +
                    "Transport: unicast;server_port=" + serverPort + "-" + (serverPort + 1) + ";source=127.0.0.1\r\n" +
                    "\r\n");
        }
    }
}
