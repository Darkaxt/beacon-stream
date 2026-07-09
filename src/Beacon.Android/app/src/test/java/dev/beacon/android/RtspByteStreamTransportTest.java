package dev.beacon.android;

import org.junit.Test;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;

import static org.junit.Assert.assertEquals;

public final class RtspByteStreamTransportTest {
    @Test
    public void writesSerializedRequestAndReadsParsedResponse() {
        ByteArrayInputStream input = input("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n");
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        RtspByteStreamTransport transport = new RtspByteStreamTransport(input, output);

        RtspResponse response = transport.transact(
            RtspRequest.options("rtsp://127.0.0.1:48010/beacon/session", 1, "127.0.0.1:48010"));

        assertEquals(200, response.statusCode());
        assertEquals("OK", response.reasonPhrase());
        assertEquals("1", response.header("CSeq"));
        assertEquals(
            "OPTIONS rtsp://127.0.0.1:48010/beacon/session RTSP/1.0\r\n" +
                "CSeq: 1\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "\r\n",
            output.toString(StandardCharsets.UTF_8));
    }

    @Test
    public void consumesContentLengthBodyBeforeNextResponse() {
        ByteArrayInputStream input = input(
            "RTSP/1.0 200 OK\r\n" +
                "CSeq: 1\r\n" +
                "Content-Length: 5\r\n" +
                "\r\n" +
                "abcde" +
                "RTSP/1.0 404 Not Found\r\n" +
                "CSeq: 2\r\n" +
                "\r\n");
        ByteArrayOutputStream output = new ByteArrayOutputStream();
        RtspByteStreamTransport transport = new RtspByteStreamTransport(input, output);

        RtspResponse first = transport.transact(
            RtspRequest.options("rtsp://127.0.0.1:48010/beacon/session", 1, "127.0.0.1:48010"));
        RtspResponse second = transport.transact(
            RtspRequest.describe("rtsp://127.0.0.1:48010/beacon/session", 2, "127.0.0.1:48010"));

        assertEquals(200, first.statusCode());
        assertEquals("5", first.header("Content-Length"));
        assertEquals(404, second.statusCode());
        assertEquals("Not Found", second.reasonPhrase());
        assertEquals("2", second.header("CSeq"));
    }

    @Test
    public void closesOwnedStreams() {
        CloseTrackingInputStream input = new CloseTrackingInputStream(bytes("RTSP/1.0 200 OK\r\n\r\n"));
        CloseTrackingOutputStream output = new CloseTrackingOutputStream();
        RtspByteStreamTransport transport = new RtspByteStreamTransport(input, output);

        transport.close();

        assertEquals(1, input.closeCount);
        assertEquals(1, output.closeCount);
    }

    @Test
    public void reportsEndOfStreamBeforeStatusLine() {
        RtspByteStreamTransport transport = new RtspByteStreamTransport(
            input(""),
            new ByteArrayOutputStream());

        try {
            transport.transact(RtspRequest.options("rtsp://127.0.0.1:48010/beacon/session", 1, "127.0.0.1:48010"));
        } catch (RtspTransportException ex) {
            assertEquals("RTSP response ended before a status line was read.", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected EOF to produce an RTSP transport diagnostic.");
    }

    private static ByteArrayInputStream input(String value) {
        return new ByteArrayInputStream(bytes(value));
    }

    private static byte[] bytes(String value) {
        return value.getBytes(StandardCharsets.UTF_8);
    }

    private static final class CloseTrackingInputStream extends ByteArrayInputStream {
        private int closeCount;

        private CloseTrackingInputStream(byte[] bytes) {
            super(bytes);
        }

        @Override
        public void close() throws IOException {
            closeCount++;
            super.close();
        }
    }

    private static final class CloseTrackingOutputStream extends ByteArrayOutputStream {
        private int closeCount;

        @Override
        public void close() throws IOException {
            closeCount++;
            super.close();
        }
    }
}
