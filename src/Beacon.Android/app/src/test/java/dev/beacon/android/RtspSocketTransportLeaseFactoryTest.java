package dev.beacon.android;

import org.junit.Test;

import java.io.ByteArrayInputStream;
import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

import static org.junit.Assert.assertEquals;

public final class RtspSocketTransportLeaseFactoryTest {
    @Test
    public void opensTransportFromSocketConnectorAndClosesSocketHandle() {
        FakeSocketHandle handle = new FakeSocketHandle("RTSP/1.0 200 OK\r\nCSeq: 1\r\n\r\n");
        RecordingSocketConnector connector = new RecordingSocketConnector(handle);
        RtspSocketTransportLeaseFactory factory = new RtspSocketTransportLeaseFactory(connector);
        GameStreamEndpointPlan plan = completePlan("rtsp://127.0.0.1:48010/beacon/session");

        RtspTransportLease lease = factory.open(plan);
        RtspResponse response = lease.transport().transact(
            RtspRequest.options(plan.rtspUri(), 1, plan.rtspHostHeader()));
        lease.close();

        assertEquals("127.0.0.1", connector.host);
        assertEquals(48010, connector.port);
        assertEquals(200, response.statusCode());
        assertEquals(
            "OPTIONS rtsp://127.0.0.1:48010/beacon/session RTSP/1.0\r\n" +
                "CSeq: 1\r\n" +
                "Host: 127.0.0.1:48010\r\n" +
                "X-GS-ClientVersion: BeaconStream\r\n" +
                "\r\n",
            handle.output.toString(StandardCharsets.UTF_8));
        assertEquals(1, handle.closeCount);
    }

    @Test
    public void reportsSocketOpenFailureAsTransportDiagnostic() {
        RtspSocketTransportLeaseFactory factory = new RtspSocketTransportLeaseFactory(new FailingSocketConnector());

        try {
            factory.open(completePlan("rtsp://127.0.0.1:48010/beacon/session"));
        } catch (RtspTransportException ex) {
            assertEquals("RTSP socket open failed: refused", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected socket open failure diagnostic.");
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

    private static final class RecordingSocketConnector implements RtspSocketConnector {
        private final RtspSocketHandle handle;
        private String host;
        private int port;

        private RecordingSocketConnector(RtspSocketHandle handle) {
            this.handle = handle;
        }

        @Override
        public RtspSocketHandle open(String host, int port) {
            this.host = host;
            this.port = port;
            return handle;
        }
    }

    private static final class FailingSocketConnector implements RtspSocketConnector {
        @Override
        public RtspSocketHandle open(String host, int port) throws IOException {
            throw new IOException("refused");
        }
    }

    private static final class FakeSocketHandle implements RtspSocketHandle {
        private final ByteArrayInputStream input;
        private final ByteArrayOutputStream output = new ByteArrayOutputStream();
        private int closeCount;

        private FakeSocketHandle(String input) {
            this.input = new ByteArrayInputStream(input.getBytes(StandardCharsets.UTF_8));
        }

        @Override
        public InputStream inputStream() {
            return input;
        }

        @Override
        public OutputStream outputStream() {
            return output;
        }

        @Override
        public void close() {
            closeCount++;
        }
    }
}
