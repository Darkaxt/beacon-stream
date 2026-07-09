package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
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
    public void startsConfiguredVideoSessionAfterRtspSuccess() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(startedRtspSessionResult());
        RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
            NativeStreamStartResult.started("Native GameStream video session started."));
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session", completeRtpMetadata()));

        assertTrue(result.success());
        assertEquals("Native GameStream video session started.", result.status());
        assertSame(rtspClient.startedPlan, videoClient.startedPlan);
        assertEquals("session-1", videoClient.startedSessionInfo.sessionId());
        assertEquals(47998, videoClient.startedSessionInfo.videoServerPort());
        assertEquals(0, rtspClient.stopCount);
    }

    @Test
    public void configuredVideoClientWithoutRtpMetadataKeepsRtspOnly() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(startedRtspSessionResult());
        RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
            NativeStreamStartResult.unsupported("should not start video"));
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session"));

        assertTrue(result.success());
        assertEquals(
            "Native GameStream RTSP session started. protocol=gamestream rtsp=rtsp://127.0.0.1:48010/beacon/session",
            result.status());
        assertEquals(0, videoClient.startCount);
        assertEquals(0, rtspClient.stopCount);
    }

    @Test
    public void configuredVideoClientWithPartialRtpMetadataFailsAndStopsRtsp() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(startedRtspSessionResult());
        RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
            NativeStreamStartResult.unsupported("metadata rejected"));
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection(
                "rtsp://127.0.0.1:48010/beacon/session",
                "\"codec\":\"h264\""));

        assertFalse(result.success());
        assertEquals("metadata rejected", result.diagnostic());
        assertEquals(1, videoClient.startCount);
        assertEquals(1, rtspClient.stopCount);
    }

    @Test
    public void videoSessionFailureStopsRtspSession() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(startedRtspSessionResult());
        RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
            NativeStreamStartResult.unsupported("RTP video failed"));
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session", completeRtpMetadata()));

        assertFalse(result.success());
        assertEquals("RTP video failed", result.diagnostic());
        assertEquals(1, rtspClient.stopCount);
        assertEquals(0, videoClient.stopCount);
    }

    @Test
    public void videoSessionStartExceptionStopsVideoAndRtspSessionAndReturnsDiagnostic() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(startedRtspSessionResult());
        RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
            NativeStreamStartResult.started("should not be returned"));
        videoClient.startFailure = new IllegalStateException("decoder exploded");
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session", completeRtpMetadata()));

        assertFalse(result.success());
        assertEquals("GameStream video session failed: decoder exploded", result.diagnostic());
        assertEquals(1, rtspClient.stopCount);
        assertEquals(1, videoClient.stopCount);
    }

    @Test
    public void stopDelegatesVideoBeforeRtspAfterSuccessfulVideoStart() {
        List<String> stopOrder = new ArrayList<>();
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(startedRtspSessionResult(), stopOrder);
        RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
            NativeStreamStartResult.started("Native GameStream video session started."),
            stopOrder);
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session", completeRtpMetadata()));
        client.stop();

        assertTrue(result.success());
        assertEquals(1, videoClient.stopCount);
        assertEquals(1, rtspClient.stopCount);
        assertEquals(Arrays.asList("video", "rtsp"), stopOrder);
    }

    @Test
    public void stopReleasesRtspWhenVideoStopThrows() {
        List<String> stopOrder = new ArrayList<>();
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(startedRtspSessionResult(), stopOrder);
        RecordingVideoSessionClient videoClient = new RecordingVideoSessionClient(
            NativeStreamStartResult.started("Native GameStream video session started."),
            stopOrder);
        videoClient.stopFailure = new IllegalStateException("video stop failed");
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient, videoClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session", completeRtpMetadata()));
        client.stop();

        assertTrue(result.success());
        assertEquals(1, videoClient.stopCount);
        assertEquals(1, rtspClient.stopCount);
        assertEquals(Arrays.asList("video", "rtsp"), stopOrder);
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
    public void stopDelegatesToSuccessfulRtspSession() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(
            GameStreamRtspSessionResult.started("RTSP session started."));
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session"));
        client.stop();

        assertTrue(result.success());
        assertEquals(1, rtspClient.stopCount);
    }

    @Test
    public void stopDoesNotDelegateAfterFailedRtspSessionStart() {
        RecordingRtspSessionClient rtspClient = new RecordingRtspSessionClient(
            GameStreamRtspSessionResult.failed("RTSP socket open failed: refused"));
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(rtspClient);

        NativeStreamStartResult result = client.start(
            completeGameStreamConnection("rtsp://127.0.0.1:48010/beacon/session"));
        client.stop();

        assertFalse(result.success());
        assertEquals(0, rtspClient.stopCount);
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
        return completeGameStreamConnection(rtspUri, "");
    }

    private static StreamConnectionDescriptor completeGameStreamConnection(String rtspUri, String metadata) {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"" + rtspUri + "\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}],\"metadata\":{" +
                metadata +
                "}}}}");
    }

    private static String completeRtpMetadata() {
        return "\"codec\":\"h264\",\"container\":\"annex-b\",\"width\":\"2560\",\"height\":\"1600\",\"fps\":\"120\"";
    }

    private static GameStreamRtspSessionResult startedRtspSessionResult() {
        return GameStreamRtspSessionResult.started(
            "RTSP session started.",
            GameStreamRtspSessionInfo.started(
                "gamestream",
                "rtsp://127.0.0.1:48010/beacon/session",
                "session-1",
                48000,
                47998,
                47999));
    }

    private static final class RecordingRtspSessionClient implements GameStreamRtspSessionClient {
        private final GameStreamRtspSessionResult result;
        private final List<String> stopOrder;
        private GameStreamEndpointPlan startedPlan;
        private int stopCount;

        private RecordingRtspSessionClient(GameStreamRtspSessionResult result) {
            this(result, null);
        }

        private RecordingRtspSessionClient(GameStreamRtspSessionResult result, List<String> stopOrder) {
            this.result = result;
            this.stopOrder = stopOrder;
        }

        @Override
        public GameStreamRtspSessionResult start(GameStreamEndpointPlan plan) {
            startedPlan = plan;
            return result;
        }

        @Override
        public void stop() {
            stopCount++;
            if (stopOrder != null) {
                stopOrder.add("rtsp");
            }
        }
    }

    private static final class RecordingVideoSessionClient implements GameStreamVideoSessionClient {
        private final NativeStreamStartResult result;
        private final List<String> stopOrder;
        private GameStreamEndpointPlan startedPlan;
        private GameStreamRtspSessionInfo startedSessionInfo;
        private RuntimeException startFailure;
        private RuntimeException stopFailure;
        private int startCount;
        private int stopCount;

        private RecordingVideoSessionClient(NativeStreamStartResult result) {
            this(result, null);
        }

        private RecordingVideoSessionClient(NativeStreamStartResult result, List<String> stopOrder) {
            this.result = result;
            this.stopOrder = stopOrder;
        }

        @Override
        public NativeStreamStartResult start(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo) {
            startCount++;
            startedPlan = plan;
            startedSessionInfo = sessionInfo;
            if (startFailure != null) {
                throw startFailure;
            }

            return result;
        }

        @Override
        public void stop() {
            stopCount++;
            if (stopOrder != null) {
                stopOrder.add("video");
            }
            if (stopFailure != null) {
                throw stopFailure;
            }
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
