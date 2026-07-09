package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

public final class GameStreamRtspSetupResultTest {
    @Test
    public void parsesSessionIdBeforeParameters() {
        GameStreamRtspSetupResult result = GameStreamRtspSetupResult.fromResponse(
            "audio",
            RtspResponse.parse(
                "RTSP/1.0 200 OK\r\n" +
                    "Session: abc123;timeout=30\r\n" +
                    "Transport: unicast;server_port=48000-48001;source=127.0.0.1\r\n" +
                    "\r\n"));

        assertTrue(result.success());
        assertEquals("abc123", result.sessionId());
        assertEquals(48000, result.serverPort());
        assertEquals("", result.diagnostic());
    }

    @Test
    public void parsesServerPortFromTransportHeader() {
        GameStreamRtspSetupResult result = GameStreamRtspSetupResult.fromResponse(
            "video",
            RtspResponse.parse(
                "RTSP/1.0 200 OK\r\n" +
                    "Session: session-1\r\n" +
                    "Transport: unicast;server_port=47998-47999;source=127.0.0.1\r\n" +
                    "\r\n"));

        assertTrue(result.success());
        assertEquals(47998, result.serverPort());
    }

    @Test
    public void reportsMissingServerPortDiagnostic() {
        GameStreamRtspSetupResult result = GameStreamRtspSetupResult.fromResponse(
            "control",
            RtspResponse.parse(
                "RTSP/1.0 200 OK\r\n" +
                    "Session: session-1\r\n" +
                    "Transport: unicast;source=127.0.0.1\r\n" +
                    "\r\n"));

        assertFalse(result.success());
        assertEquals(
            "RTSP SETUP control response did not include a valid server_port in the Transport header.",
            result.diagnostic());
    }

    @Test
    public void reportsInvalidServerPortDiagnostic() {
        GameStreamRtspSetupResult result = GameStreamRtspSetupResult.fromResponse(
            "control",
            RtspResponse.parse(
                "RTSP/1.0 200 OK\r\n" +
                    "Session: session-1\r\n" +
                    "Transport: unicast;server_port=99999-100000;source=127.0.0.1\r\n" +
                    "\r\n"));

        assertFalse(result.success());
        assertEquals(
            "RTSP SETUP control response did not include a valid server_port in the Transport header.",
            result.diagnostic());
    }
}
