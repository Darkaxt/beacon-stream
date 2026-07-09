package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class GameStreamRtspHandshakeClientTest {
    @Test
    public void sendsOptionsThenDescribeOverTransport() {
        RecordingRtspTransport transport = new RecordingRtspTransport(
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
            RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 2\r\nContent-Type: application/sdp\r\n\r\n"));
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(transport);

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertTrue(result.success());
        assertEquals(
            "RTSP handshake completed. protocol=gamestream rtsp=rtsp://127.0.0.1:48010/beacon/session",
            result.status());
        assertEquals(2, transport.requests.size());
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
    public void failsWhenOptionsReturnsNonSuccessStatus() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(RtspResponse.parse("RTSP/1.0 503 Busy\r\nCSeq: 1\r\n\r\n")));

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP OPTIONS failed with status 503 Busy.", result.diagnostic());
    }

    @Test
    public void failsWhenDescribeReturnsNonSuccessStatus() {
        GameStreamRtspHandshakeClient client = new GameStreamRtspHandshakeClient(
            new RecordingRtspTransport(
                RtspResponse.parse("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n"),
                RtspResponse.parse("RTSP/1.0 404 Not Found\r\nCSeq: 2\r\n\r\n")));

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP DESCRIBE failed with status 404 Not Found.", result.diagnostic());
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
