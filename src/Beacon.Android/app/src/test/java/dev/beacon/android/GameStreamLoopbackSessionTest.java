package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;
import java.util.ArrayDeque;
import java.util.Queue;

import static org.junit.Assert.assertArrayEquals;
import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertTrue;

public final class GameStreamLoopbackSessionTest {
    @Test
    public void loopbackRtspSetupFeedsH264RtpSampleProvider() {
        LoopbackDatagramSocket audio = new LoopbackDatagramSocket(61000);
        LoopbackDatagramSocket video = new LoopbackDatagramSocket(
            61002,
            rtpPacket(1, 90000L, false, new byte[] {0x41, 0x11}),
            rtpPacket(2, 90000L, true, new byte[] {0x41, 0x22}));
        LoopbackDatagramSocket control = new LoopbackDatagramSocket(61004);
        LoopbackRtspLeaseFactory leaseFactory = new LoopbackRtspLeaseFactory();
        RecordingRtpVideoConsumer consumer = new RecordingRtpVideoConsumer();
        GameStreamNativeStreamClient client = new GameStreamNativeStreamClient(
            new GameStreamRtspTransportSessionClient(
                leaseFactory,
                () -> GameStreamRtpPortLease.open(new LoopbackDatagramSocketFactory(audio, video, control)),
                new FixedSdpPayloadProvider("v=0\r\ns=Beacon Loopback\r\n")),
            new GameStreamRtpVideoSessionClient(
                new GameStreamUdpRtpPacketSourceFactory(),
                consumer));

        NativeStreamStartResult result = client.start(completeGameStreamConnection());

        assertTrue(result.diagnostic(), result.success());
        EncodedVideoSample sample = consumer.sampleProvider.nextSample();
        client.stop();

        assertArrayEquals(
            concat(start(), new byte[] {0x41, 0x11}, start(), new byte[] {0x41, 0x22}),
            sample.data());
        assertEquals(0L, sample.presentationTimeUs());
        assertEquals(1, consumer.stopCount);
        assertEquals(1, leaseFactory.closeCount);
        assertEquals(1, audio.closeCount);
        assertEquals(1, video.closeCount);
        assertEquals(1, control.closeCount);
    }

    private static StreamConnectionDescriptor completeGameStreamConnection() {
        return StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}],\"metadata\":{" +
                "\"codec\":\"h264\"," +
                "\"container\":\"annex-b\"," +
                "\"width\":\"2560\"," +
                "\"height\":\"1600\"," +
                "\"fps\":\"120\"" +
                "}}}}");
    }

    private static byte[] rtpPacket(int sequenceNumber, long timestamp, boolean marker, byte[] payload) {
        byte[] bytes = new byte[12 + payload.length];
        bytes[0] = (byte) 0x80;
        bytes[1] = (byte) (0x60 | (marker ? 0x80 : 0));
        bytes[2] = (byte) ((sequenceNumber >>> 8) & 0xFF);
        bytes[3] = (byte) (sequenceNumber & 0xFF);
        bytes[4] = (byte) ((timestamp >>> 24) & 0xFF);
        bytes[5] = (byte) ((timestamp >>> 16) & 0xFF);
        bytes[6] = (byte) ((timestamp >>> 8) & 0xFF);
        bytes[7] = (byte) (timestamp & 0xFF);
        bytes[11] = 0x01;
        System.arraycopy(payload, 0, bytes, 12, payload.length);
        return bytes;
    }

    private static byte[] start() {
        return new byte[] {0, 0, 0, 1};
    }

    private static byte[] concat(byte[]... arrays) {
        int length = 0;
        for (byte[] array : arrays) {
            length += array.length;
        }

        byte[] result = new byte[length];
        int offset = 0;
        for (byte[] array : arrays) {
            System.arraycopy(array, 0, result, offset, array.length);
            offset += array.length;
        }

        return result;
    }

    private static final class LoopbackRtspLeaseFactory implements RtspTransportLeaseFactory {
        private int closeCount;

        @Override
        public RtspTransportLease open(GameStreamEndpointPlan plan) {
            return RtspTransportLease.of(new LoopbackRtspTransport(), () -> closeCount++);
        }
    }

    private static final class LoopbackRtspTransport implements RtspTransport {
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

    private static final class LoopbackDatagramSocketFactory implements RtpDatagramSocketFactory {
        private final RtpDatagramSocket[] sockets;
        private int nextSocket;

        private LoopbackDatagramSocketFactory(RtpDatagramSocket... sockets) {
            this.sockets = sockets;
        }

        @Override
        public RtpDatagramSocket bind(int localPort) {
            return sockets[nextSocket++];
        }
    }

    private static final class LoopbackDatagramSocket implements RtpDatagramSocket {
        private final int localPort;
        private final Queue<byte[]> datagrams = new ArrayDeque<>();
        private int closeCount;

        private LoopbackDatagramSocket(int localPort, byte[]... datagrams) {
            this.localPort = localPort;
            for (byte[] datagram : datagrams) {
                this.datagrams.add(datagram);
            }
        }

        @Override
        public int receive(byte[] buffer) throws IOException {
            byte[] datagram = datagrams.poll();
            if (datagram == null) {
                throw new IOException("no loopback RTP datagram queued");
            }

            System.arraycopy(datagram, 0, buffer, 0, datagram.length);
            return datagram.length;
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

    private static final class RecordingRtpVideoConsumer implements GameStreamRtpVideoConsumer {
        private EncodedVideoSampleProvider sampleProvider;
        private int stopCount;

        @Override
        public NativeStreamStartResult start(
            GameStreamEndpointPlan plan,
            GameStreamRtspSessionInfo sessionInfo,
            EncodedVideoSampleProvider sampleProvider) {
            this.sampleProvider = sampleProvider;
            return NativeStreamStartResult.started("Loopback GameStream RTP video session started.");
        }

        @Override
        public void stop() {
            stopCount++;
        }
    }
}
