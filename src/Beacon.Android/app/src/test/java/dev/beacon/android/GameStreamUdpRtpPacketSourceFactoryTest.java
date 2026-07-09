package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class GameStreamUdpRtpPacketSourceFactoryTest {
    @Test
    public void usesLeasedVideoSocketWithoutRebinding() {
        RecordingDatagramSocket audio = new RecordingDatagramSocket(61000);
        RecordingDatagramSocket video = new RecordingDatagramSocket(61002);
        RecordingDatagramSocket control = new RecordingDatagramSocket(61004);
        GameStreamRtpPortLease lease = GameStreamRtpPortLease.open(
            new RecordingDatagramSocketFactory(audio, video, control));
        RecordingDatagramSocketFactory fallbackSocketFactory = new RecordingDatagramSocketFactory();
        GameStreamUdpRtpPacketSourceFactory factory = new GameStreamUdpRtpPacketSourceFactory(fallbackSocketFactory);

        RtpPacketSource source = factory.create(completePlan(), sessionInfoWithClientPorts(lease));
        source.close();
        lease.close();

        assertTrue(source instanceof RtpDatagramPacketSource);
        assertEquals(-1, fallbackSocketFactory.boundPort);
        assertEquals(1, audio.closeCount);
        assertEquals(1, video.closeCount);
        assertEquals(1, control.closeCount);
    }

    @Test
    public void legacySessionInfoBindsNegotiatedVideoClientPort() {
        RecordingDatagramSocketFactory socketFactory = new RecordingDatagramSocketFactory();
        GameStreamUdpRtpPacketSourceFactory factory = new GameStreamUdpRtpPacketSourceFactory(socketFactory);

        RtpPacketSource source = factory.create(completePlan(), sessionInfoWithClientPorts());

        assertTrue(source instanceof RtpDatagramPacketSource);
        assertEquals(50002, socketFactory.boundPort);
    }

    @Test
    public void unavailableVideoSocketLeaseReturnsDiagnostic() {
        GameStreamUdpRtpPacketSourceFactory factory = new GameStreamUdpRtpPacketSourceFactory(
            new RecordingDatagramSocketFactory());

        try {
            factory.create(
                completePlan(),
                sessionInfoWithClientPorts(GameStreamRtpPortLease.staticPorts(50000, 50002, 50004)));
        } catch (IllegalStateException ex) {
            assertEquals("GameStream RTP video socket lease is unavailable.", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected unavailable video socket lease diagnostic.");
    }

    @Test
    public void missingVideoClientPortReturnsDiagnostic() {
        GameStreamUdpRtpPacketSourceFactory factory = new GameStreamUdpRtpPacketSourceFactory(
            new RecordingDatagramSocketFactory());

        try {
            factory.create(completePlan(), legacySessionInfoWithoutClientPorts());
        } catch (IllegalStateException ex) {
            assertEquals("GameStream RTP video client port is unavailable.", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected missing video client port diagnostic.");
    }

    @Test
    public void bindFailureReturnsDiagnostic() {
        GameStreamUdpRtpPacketSourceFactory factory = new GameStreamUdpRtpPacketSourceFactory(
            new ThrowingDatagramSocketFactory());

        try {
            factory.create(completePlan(), sessionInfoWithClientPorts());
        } catch (IllegalStateException ex) {
            assertEquals("RTP UDP socket open failed: address in use", ex.getMessage());
            return;
        }

        throw new AssertionError("Expected UDP socket open diagnostic.");
    }

    private static GameStreamEndpointPlan completePlan() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");
        return GameStreamEndpointPlan.from(descriptor);
    }

    private static GameStreamRtspSessionInfo sessionInfoWithClientPorts() {
        return sessionInfoWithClientPorts(null);
    }

    private static GameStreamRtspSessionInfo sessionInfoWithClientPorts(GameStreamRtpPortLease rtpPortLease) {
        return GameStreamRtspSessionInfo.startedWithClientPorts(
            "gamestream",
            "rtsp://127.0.0.1:48010/beacon/session",
            "session-1",
            50000,
            48000,
            50002,
            47998,
            50004,
            47999,
            rtpPortLease);
    }

    private static GameStreamRtspSessionInfo legacySessionInfoWithoutClientPorts() {
        return GameStreamRtspSessionInfo.started(
            "gamestream",
            "rtsp://127.0.0.1:48010/beacon/session",
            "session-1",
            48000,
            47998,
            47999);
    }

    private static final class RecordingDatagramSocketFactory implements RtpDatagramSocketFactory {
        private final RtpDatagramSocket[] sockets;
        private int boundPort = -1;
        private int nextSocket;

        private RecordingDatagramSocketFactory(RtpDatagramSocket... sockets) {
            this.sockets = sockets;
        }

        @Override
        public RtpDatagramSocket bind(int localPort) {
            boundPort = localPort;
            if (nextSocket < sockets.length) {
                return sockets[nextSocket++];
            }

            return new RecordingDatagramSocket(localPort);
        }
    }

    private static final class ThrowingDatagramSocketFactory implements RtpDatagramSocketFactory {
        @Override
        public RtpDatagramSocket bind(int localPort) throws IOException {
            throw new IOException("address in use");
        }
    }

    private static final class RecordingDatagramSocket implements RtpDatagramSocket {
        private final int localPort;
        private int closeCount;

        private RecordingDatagramSocket(int localPort) {
            this.localPort = localPort;
        }

        @Override
        public int receive(byte[] buffer) {
            return 0;
        }

        @Override
        public int localPort() {
            return localPort;
        }

        @Override
        public void close() {
            closeCount++;
        }
    }
}
