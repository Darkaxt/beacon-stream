package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;

public final class RtspRequestTest {
    @Test
    public void serializesOptionsRequestWithGameStreamHeaders() {
        RtspRequest request = RtspRequest.options("rtsp://127.0.0.1:48010/beacon/session", 1, "127.0.0.1:48010");

        assertEquals(
            "OPTIONS rtsp://127.0.0.1:48010/beacon/session RTSP/1.0\r\n" +
                "CSeq: 1\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "\r\n",
            request.serialize());
    }

    @Test
    public void serializesDescribeRequestWithSdpHeaders() {
        RtspRequest request = RtspRequest.describe("rtsp://127.0.0.1:48010/beacon/session", 2, "127.0.0.1:48010");

        assertEquals(
            "DESCRIBE rtsp://127.0.0.1:48010/beacon/session RTSP/1.0\r\n" +
                "CSeq: 2\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Accept: application/sdp\r\n" +
                "If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT\r\n" +
                "\r\n",
            request.serialize());
    }

    @Test
    public void serializesSetupRequestWithoutSession() {
        RtspRequest request = RtspRequest.setup("streamid=audio/0/0", 3, "127.0.0.1:48010", "");

        assertEquals(
            "SETUP streamid=audio/0/0 RTSP/1.0\r\n" +
                "CSeq: 3\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Transport: unicast;X-GS-ClientPort=50000-50001\r\n" +
                "If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT\r\n" +
                "\r\n",
            request.serialize());
    }

    @Test
    public void serializesSetupRequestWithSession() {
        RtspRequest request = RtspRequest.setup("streamid=video/0/0", 4, "127.0.0.1:48010", "abc123");

        assertEquals(
            "SETUP streamid=video/0/0 RTSP/1.0\r\n" +
                "CSeq: 4\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Session: abc123\r\n" +
                "Transport: unicast;X-GS-ClientPort=50000-50001\r\n" +
                "If-Modified-Since: Thu, 01 Jan 1970 00:00:00 GMT\r\n" +
                "\r\n",
            request.serialize());
    }

    @Test
    public void serializesAnnounceRequestWithSdpPayload() {
        String payload = "v=0\r\ns=Beacon \u03c0\r\n";
        RtspRequest request = RtspRequest.announce(
            "streamid=control/13/0",
            6,
            "127.0.0.1:48010",
            "session-1",
            payload);

        assertEquals(
            "ANNOUNCE streamid=control/13/0 RTSP/1.0\r\n" +
                "CSeq: 6\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Session: session-1\r\n" +
                "Content-type: application/sdp\r\n" +
                "Content-length: 18\r\n" +
                "\r\n" +
                payload,
            request.serialize());
    }

    @Test
    public void serializesPlayRequestWithSession() {
        RtspRequest request = RtspRequest.play("/", 7, "127.0.0.1:48010", "session-1");

        assertEquals(
            "PLAY / RTSP/1.0\r\n" +
                "CSeq: 7\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "Session: session-1\r\n" +
                "\r\n",
            request.serialize());
    }
}
