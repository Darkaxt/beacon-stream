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
}
