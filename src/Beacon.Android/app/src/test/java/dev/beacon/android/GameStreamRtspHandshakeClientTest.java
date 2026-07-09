package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;
import java.nio.charset.StandardCharsets;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class GameStreamRtspHandshakeClientTest {
    private static final String SdpPayload = "v=0\r\ns=Beacon Test\r\n";

    @Test
    public void sendsOptionsThenDescribeOverTransport() {
        RecordingRtspTransport transport = new RecordingRtspTransport(
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"),
            setupResponse("3", "session-1", 48000),
            setupResponse("4", "session-1", 47998),
            setupResponse("5", "session-1", 47999),
            okResponse("6"),
            okResponse("7"));
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            transport,
            new FixedSdpPayloadProvider(SdpPayload),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertTrue(result.success());
        assertEquals(
            "RTSP play started. protocol=gamestream rtsp=rtsp://127.0.0.1:48010/beacon/session session=session-1 audioPort=48000 videoPort=47998 controlPort=47999",
            result.status());
        assertSessionInfo(result);
        assertEquals(7, transport.requests.size());
        assertEquals(
            "OPTIONS rtsp://127.0.0.1:48010/beacon/session RTSP/1.0\r\n" +
                "CSeq: 1\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "\r\n",
            transport.requests.get(0));
        assertEquals(
            "DESCRIBE rtsp://127.0.0.1:48010/beacon/session RTSP/1.0\r\n" +
                "CSeq: 2\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Accept: application/sdp\r\n" +
                "If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT\r\n" +
                "\r\n",
            transport.requests.get(1));
    }

    @Test
    public void sendsSetupAnnounceAndPlayRequestsThenCapturesSessionAndServerPorts() {
        RecordingRtspTransport transport = new RecordingRtspTransport(
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"),
            setupResponse("3", "session-1", 48000),
            setupResponse("4", "session-1", 47998),
            setupResponse("5", "session-1", 47999),
            okResponse("6"),
            okResponse("7"));
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            transport,
            new FixedSdpPayloadProvider(SdpPayload),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertTrue(result.success());
        assertEquals(
            "RTSP play started. protocol=gamestream rtsp=rtsp://127.0.0.1:48010/beacon/session session=session-1 audioPort=48000 videoPort=47998 controlPort=47999",
            result.status());
        assertSessionInfo(result);
        assertEquals(7, transport.requests.size());
        assertEquals(
            "SETUP streamid=audio/0/0 RTSP/1.0\r\n" +
                "CSeq: 3\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Transport: unicast;X-GS-ClientPort=50000-50001\r\n" +
                "If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT\r\n" +
                "\r\n",
            transport.requests.get(2));
        assertTrue(transport.requests.get(3).contains("SETUP streamid=video/0/0 RTSP/1.0\r\n"));
        assertTrue(transport.requests.get(3).contains("Session: session-1\r\n"));
        assertTrue(transport.requests.get(3).contains("Transport: unicast;X-GS-ClientPort=50002-50003\r\n"));
        assertTrue(transport.requests.get(4).contains("SETUP streamid=control/13/0 RTSP/1.0\r\n"));
        assertTrue(transport.requests.get(4).contains("Session: session-1\r\n"));
        assertTrue(transport.requests.get(4).contains("Transport: unicast;X-GS-ClientPort=50004-50005\r\n"));
        assertEquals(
            "ANNOUNCE streamid=control/13/0 RTSP/1.0\r\n" +
                "CSeq: 6\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Session: session-1\r\n" +
                "Content-type: application/sdp\r\n" +
                "Content-length: 20\r\n" +
                "\r\n" +
                SdpPayload,
            transport.requests.get(5));
        assertEquals(
            "PLAY / RTSP/1.0\r\n" +
                "CSeq: 7\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Session: session-1\r\n" +
                "\r\n",
            transport.requests.get(6));
    }

    @Test
    public void capturesH264ParameterSetsFromDescribeSdp() {
        String sdp =
            "v=0\r\n" +
                "m=video 0 RTP/AVP 97\r\n" +
                "a=rtpmap:97 H264/90000\r\n" +
                "a=fmtp:97 packetization-mode=1;sprop-parameter-sets=Z0IAHg==,aM4G4g==\r\n";
        RecordingRtspTransport transport = new RecordingRtspTransport(
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
            describeResponse(sdp),
            setupResponse("3", "session-1", 48000),
            setupResponse("4", "session-1", 47998),
            setupResponse("5", "session-1", 47999),
            okResponse("6"),
            okResponse("7"));
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            transport,
            new FixedSdpPayloadProvider(SdpPayload),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertTrue(result.success());
        assertEquals("Z0IAHg==,aM4G4g==", result.sessionInfo().h264SpropParameterSets());
    }

    @Test
    public void leavesH264ParameterSetsEmptyWhenDescribeSdpDoesNotAdvertiseThem() {
        String sdp =
            "v=0\r\n" +
                "m=video 0 RTP/AVP 97\r\n" +
                "a=rtpmap:97 H264/90000\r\n";
        RecordingRtspTransport transport = new RecordingRtspTransport(
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
            describeResponse(sdp),
            setupResponse("3", "session-1", 48000),
            setupResponse("4", "session-1", 47998),
            setupResponse("5", "session-1", 47999),
            okResponse("6"),
            okResponse("7"));
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            transport,
            new FixedSdpPayloadProvider(SdpPayload),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertTrue(result.success());
        assertEquals("", result.sessionInfo().h264SpropParameterSets());
    }

    @Test
    public void failsWhenOptionsReturnsNonSuccessStatus() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(RtspResponse.parse("RTSP/1.0 503 Busy\r\nCSeq: 1\r\n\r\n")),
            GameStreamRtspSdpPayloadProvider.diagnostic(),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP OPTIONS failed with status 503 Busy.", result.diagnostic());
    }

    @Test
    public void failsWithTransportDiagnosticWhenTransportThrows() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new ThrowingRtspTransport(),
            GameStreamRtspSdpPayloadProvider.diagnostic(),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP write failed: disk full", result.diagnostic());
    }

    @Test
    public void failsWhenDescribeReturnsNonSuccessStatus() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
                RtspResponse.parse("RTSP/1.0 404 Not Found\r\nCSeq: 2\r\n\r\n")),
            GameStreamRtspSdpPayloadProvider.diagnostic(),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP DESCRIBE failed with status 404 Not Found.", result.diagnostic());
    }

    @Test
    public void failsWhenSetupReturnsNonSuccessStatus() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"),
                setupResponse("3", "session-1", 48000),
                RtspResponse.parse("RTSP/1.0 503 Busy\r\nCSeq: 4\r\n\r\n")),
            GameStreamRtspSdpPayloadProvider.diagnostic(),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP SETUP video failed with status 503 Busy.", result.diagnostic());
    }

    @Test
    public void failsWhenSetupTransportHeaderCannotBeParsed() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"),
                RtspResponse.parse(
                    "RTSP/1.0 200 OK\r\n" +
                        "CSeq: 3\r\n" +
                        "Session: session-1\r\n" +
                        "Transport: unicast;source=127.0.0.1\r\n" +
                        "\r\n")),
            GameStreamRtspSdpPayloadProvider.diagnostic(),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals(
            "RTSP SETUP audio response did not include a valid server_port in the Transport header.",
            result.diagnostic());
    }

    @Test
    public void failsWhenSdpPayloadProviderReturnsEmptyPayload() {
        RecordingRtspTransport transport = new RecordingRtspTransport(
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"),
            setupResponse("3", "session-1", 48000),
            setupResponse("4", "session-1", 47998),
            setupResponse("5", "session-1", 47999));
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            transport,
            new FixedSdpPayloadProvider(" \r\n "),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP ANNOUNCE payload is empty.", result.diagnostic());
        assertEquals(5, transport.requests.size());
    }

    @Test
    public void failsWhenAnnounceReturnsNonSuccessStatus() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"),
                setupResponse("3", "session-1", 48000),
                setupResponse("4", "session-1", 47998),
                setupResponse("5", "session-1", 47999),
                RtspResponse.parse("RTSP/1.0 503 Busy\r\nCSeq: 6\r\n\r\n")),
            new FixedSdpPayloadProvider(SdpPayload),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP ANNOUNCE failed with status 503 Busy.", result.diagnostic());
    }

    @Test
    public void failsWhenPlayReturnsNonSuccessStatus() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"),
                setupResponse("3", "session-1", 48000),
                setupResponse("4", "session-1", 47998),
                setupResponse("5", "session-1", 47999),
                okResponse("6"),
                RtspResponse.parse("RTSP/1.0 404 Not Found\r\nCSeq: 7\r\n\r\n")),
            new FixedSdpPayloadProvider(SdpPayload),
            testPortLease());

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP PLAY failed with status 404 Not Found.", result.diagnostic());
    }

    private static GameStreamEndpointPlan completePlan(String rtspUri) {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"" + rtspUri + "\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");
        return GameStreamEndpointPlan.from(descriptor);
    }

    private static void assertSessionInfo(GameStreamRtspSessionResult result) {
        assertTrue(result.sessionInfo().present());
        assertEquals("gamestream", result.sessionInfo().protocol());
        assertEquals("rtsp://127.0.0.1:48010/beacon/session", result.sessionInfo().rtspUri());
        assertEquals("session-1", result.sessionInfo().sessionId());
        assertEquals(50000, result.sessionInfo().audioClientPort());
        assertEquals(48000, result.sessionInfo().audioServerPort());
        assertEquals(50002, result.sessionInfo().videoClientPort());
        assertEquals(47998, result.sessionInfo().videoServerPort());
        assertEquals(50004, result.sessionInfo().controlClientPort());
        assertEquals(47999, result.sessionInfo().controlServerPort());
    }

    private static GameStreamRtpPortLease testPortLease() {
        return GameStreamRtpPortLease.staticPorts(50000, 50002, 50004);
    }

    private static RtspResponse setupResponse(String cseq, String sessionId, int serverPort) {
        return RtspResponse.parse(
            "RTSP/1.0 200 OK\r\n" +
                "CSeq: " + cseq + "\r\n" +
                "Session: " + sessionId + ";timeout=30\r\n" +
                "Transport: unicast;server_port=" + serverPort + "-" + (serverPort + 1) + ";source=127.0.0.1\r\n" +
                "\r\n");
    }

    private static RtspResponse okResponse(String cseq) {
        return RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: " + cseq + "\r\n\r\n");
    }

    private static RtspResponse describeResponse(String sdp) {
        return RtspResponse.parse(
            "RTSP/1.0 200 OK\r\n" +
                "CSeq: 2\r\n" +
                "Content-Type: application/sdp\r\n" +
                "Content-Length: " + sdp.getBytes(StandardCharsets.UTF_8).length + "\r\n" +
                "\r\n" +
                sdp);
    }

    private static final class FixedSdpPayloadProvider implements GameStreamRtspSdpPayloadProvider {
        private final String payload;

        private FixedSdpPayloadProvider(String payload) {
            this.payload = payload;
        }

        @Override
        public String createSdpPayload(
            GameStreamEndpointPlan plan,
            String sessionId,
            int audioPort,
            int videoPort,
            int controlPort) {
            return payload;
        }
    }

    private static final class ThrowingRtspTransport implements RtspTransport {
        @Override
        public RtspResponse transact(RtspRequest request) {
            throw new RtspTransportException("RTSP write failed: disk full");
        }
    }

    private static final class RecordingRtspTransport implements RtspTransport {
        private final List<RtspResponse> responses;
        private final List<String> requests = new ArrayList<>();
        private int nextResponse;

        private RecordingRtspTransport(RtspResponse... responses) {
            this.responses = Arrays.asList(responses);
        }

        @Override
        public RtspResponse transact(RtspRequest request) {
            requests.add(request.serialize());
            return responses.get(nextResponse++);
        }
    }
}
