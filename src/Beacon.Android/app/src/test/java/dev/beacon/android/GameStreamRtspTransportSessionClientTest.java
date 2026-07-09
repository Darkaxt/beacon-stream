package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class GameStreamRtspTransportSessionClientTest {
    @Test
    public void successfulHandshakeRetainsLeaseUntilStop() {
        RecordingLease lease = new RecordingLease(new SuccessfulRtspTransport());
        RecordingLeaseFactory factory = new RecordingLeaseFactory(lease);
        GameStreamRtspTransportSessionClient client = new GameStreamRtspTransportSessionClient(
            factory,
            new FixedSdpPayloadProvider("v=0\r\ns=Beacon Test\r\n"));
        GameStreamEndpointPlan plan = completePlan("rtsp://127.0.0.1:48010/beacon/session");

        GameStreamRtspSessionResult result = client.start(plan);

        assertTrue(result.success());
        assertSame(plan, factory.openedPlans.get(0));
        assertEquals(0, lease.closeCount);

        client.stop();

        assertEquals(1, lease.closeCount);
    }

    @Test
    public void failedHandshakeClosesLeaseImmediately() {
        RecordingLease lease = new RecordingLease(new FailingRtspTransport());
        GameStreamRtspTransportSessionClient client = new GameStreamRtspTransportSessionClient(
            new RecordingLeaseFactory(lease),
            new FixedSdpPayloadProvider("v=0\r\ns=Beacon Test\r\n"));

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP OPTIONS failed with status 503 Busy.", result.diagnostic());
        assertEquals(1, lease.closeCount);
    }

    @Test
    public void replacementSessionClosesPreviousLease() {
        RecordingLease firstLease = new RecordingLease(new SuccessfulRtspTransport());
        RecordingLease secondLease = new RecordingLease(new SuccessfulRtspTransport());
        GameStreamRtspTransportSessionClient client = new GameStreamRtspTransportSessionClient(
            new RecordingLeaseFactory(firstLease, secondLease),
            new FixedSdpPayloadProvider("v=0\r\ns=Beacon Test\r\n"));
        GameStreamEndpointPlan plan = completePlan("rtsp://127.0.0.1:48010/beacon/session");

        GameStreamRtspSessionResult first = client.start(plan);
        GameStreamRtspSessionResult second = client.start(plan);

        assertTrue(first.success());
        assertTrue(second.success());
        assertEquals(1, firstLease.closeCount);
        assertEquals(0, secondLease.closeCount);
    }

    @Test
    public void factoryFailureReturnsDiagnostic() {
        GameStreamRtspTransportSessionClient client = new GameStreamRtspTransportSessionClient(
            new ThrowingLeaseFactory(),
            new FixedSdpPayloadProvider("v=0\r\ns=Beacon Test\r\n"));

        GameStreamRtspSessionResult result = client.start(completePlan("rtsp://127.0.0.1:48010/beacon/session"));

        assertFalse(result.success());
        assertEquals("RTSP socket open failed: refused", result.diagnostic());
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

    private static final class RecordingLeaseFactory implements RtspTransportLeaseFactory {
        private final List<RtspTransportLease> leases;
        private final List<GameStreamEndpointPlan> openedPlans = new ArrayList<>();
        private int nextLease;

        private RecordingLeaseFactory(RtspTransportLease... leases) {
            this.leases = Arrays.asList(leases);
        }

        @Override
        public RtspTransportLease open(GameStreamEndpointPlan plan) {
            openedPlans.add(plan);
            return leases.get(nextLease++);
        }
    }

    private static final class ThrowingLeaseFactory implements RtspTransportLeaseFactory {
        @Override
        public RtspTransportLease open(GameStreamEndpointPlan plan) {
            throw new RtspTransportException("RTSP socket open failed: refused");
        }
    }

    private static final class RecordingLease implements RtspTransportLease {
        private final RtspTransport transport;
        private int closeCount;

        private RecordingLease(RtspTransport transport) {
            this.transport = transport;
        }

        @Override
        public RtspTransport transport() {
            return transport;
        }

        @Override
        public void close() {
            closeCount++;
        }
    }

    private static final class SuccessfulRtspTransport implements RtspTransport {
        private int cseq = 1;

        @Override
        public RtspResponse transact(RtspRequest request) {
            int current = cseq++;
            if (current <= 2 || current >= 6) {
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

    private static final class FailingRtspTransport implements RtspTransport {
        @Override
        public RtspResponse transact(RtspRequest request) {
            return RtspResponse.parse("RTSP/1.0 503 Busy\r\nCSeq: 1\r\n\r\n");
        }
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
}
