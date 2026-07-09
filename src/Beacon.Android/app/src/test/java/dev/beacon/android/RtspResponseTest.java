package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;

public final class RtspResponseTest {
    @Test
    public void parsesStatusLineAndHeadersCaseInsensitively() {
        RtspResponse response = RtspResponse.parse(
            "RTSP/1.0 200 OK\r\n" +
                "CSeq: 1\r\n" +
                "Session: abc123\r\n" +
                "\r\n");

        assertEquals(200, response.statusCode());
        assertEquals("OK", response.reasonPhrase());
        assertEquals("1", response.header("cseq"));
        assertEquals("abc123", response.header("SESSION"));
    }

    @Test
    public void rejectsMalformedStatusLine() {
        try {
            RtspResponse.parse("NOT RTSP\r\n\r\n");
        } catch (IllegalArgumentException ex) {
            assertEquals("Invalid RTSP status line: NOT RTSP", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected malformed response to be rejected.");
    }
}
