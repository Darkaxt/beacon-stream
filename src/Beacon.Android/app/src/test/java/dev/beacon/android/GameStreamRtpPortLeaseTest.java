package dev.beacon.android;

import org.junit.Test;

import java.io.IOException;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertSame;

public final class GameStreamRtpPortLeaseTest {
    @Test
    public void opensDynamicSocketsAndExposesLocalPorts() {
        RecordingRtpDatagramSocket audio = new RecordingRtpDatagramSocket(61000);
        RecordingRtpDatagramSocket video = new RecordingRtpDatagramSocket(61002);
        RecordingRtpDatagramSocket control = new RecordingRtpDatagramSocket(61004);
        RecordingRtpDatagramSocketFactory factory = new RecordingRtpDatagramSocketFactory(
            audio,
            video,
            control);

        GameStreamRtpPortLease lease = GameStreamRtpPortLease.open(factory);

        assertEquals(Arrays.asList(0, 0, 0), factory.boundPorts);
        assertEquals(61000, lease.audioClientPort());
        assertEquals(61002, lease.videoClientPort());
        assertEquals(61004, lease.controlClientPort());
    }

    @Test
    public void closeReleasesEachOwnedSocketOnce() {
        RecordingRtpDatagramSocket audio = new RecordingRtpDatagramSocket(61000);
        RecordingRtpDatagramSocket video = new RecordingRtpDatagramSocket(61002);
        RecordingRtpDatagramSocket control = new RecordingRtpDatagramSocket(61004);
        GameStreamRtpPortLease lease = GameStreamRtpPortLease.open(
            new RecordingRtpDatagramSocketFactory(audio, video, control));

        lease.close();
        lease.close();

        assertEquals(1, audio.closeCount);
        assertEquals(1, video.closeCount);
        assertEquals(1, control.closeCount);
    }

    @Test
    public void takeVideoSocketTransfersVideoOwnershipOutOfLease() {
        RecordingRtpDatagramSocket audio = new RecordingRtpDatagramSocket(61000);
        RecordingRtpDatagramSocket video = new RecordingRtpDatagramSocket(61002);
        RecordingRtpDatagramSocket control = new RecordingRtpDatagramSocket(61004);
        GameStreamRtpPortLease lease = GameStreamRtpPortLease.open(
            new RecordingRtpDatagramSocketFactory(audio, video, control));

        RtpDatagramSocket transferredVideo = lease.takeVideoSocket();
        lease.close();
        transferredVideo.close();

        assertSame(video, transferredVideo);
        assertEquals(1, audio.closeCount);
        assertEquals(1, video.closeCount);
        assertEquals(1, control.closeCount);
    }

    @Test
    public void partialOpenFailureClosesAlreadyOpenedSockets() {
        RecordingRtpDatagramSocket audio = new RecordingRtpDatagramSocket(61000);
        RecordingRtpDatagramSocketFactory factory = new RecordingRtpDatagramSocketFactory(audio);
        factory.failure = new IOException("video busy");

        try {
            GameStreamRtpPortLease.open(factory);
        } catch (IllegalStateException ex) {
            assertEquals("RTP UDP port lease failed: video busy", ex.getMessage());
            assertEquals(1, audio.closeCount);
            return;
        }

        throw new AssertionError("Expected RTP UDP port lease failure.");
    }

    private static final class RecordingRtpDatagramSocketFactory implements RtpDatagramSocketFactory {
        private final RtpDatagramSocket[] sockets;
        private final List<Integer> boundPorts = new ArrayList<>();
        private IOException failure;
        private int nextSocket;

        private RecordingRtpDatagramSocketFactory(RtpDatagramSocket... sockets) {
            this.sockets = sockets;
        }

        @Override
        public RtpDatagramSocket bind(int localPort) throws IOException {
            boundPorts.add(localPort);
            if (nextSocket >= sockets.length) {
                throw failure == null ? new IOException("no socket") : failure;
            }

            return sockets[nextSocket++];
        }
    }

    private static final class RecordingRtpDatagramSocket implements RtpDatagramSocket {
        private final int localPort;
        private int closeCount;

        private RecordingRtpDatagramSocket(int localPort) {
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
