package dev.beacon.android;

import java.io.IOException;
import java.net.DatagramPacket;
import java.net.DatagramSocket;

public final class JavaRtpDatagramSocketFactory implements RtpDatagramSocketFactory {
    @Override
    public RtpDatagramSocket bind(int localPort) throws IOException {
        return new JavaRtpDatagramSocket(new DatagramSocket(localPort));
    }

    private static final class JavaRtpDatagramSocket implements RtpDatagramSocket {
        private final DatagramSocket socket;

        private JavaRtpDatagramSocket(DatagramSocket socket) {
            this.socket = socket;
        }

        @Override
        public int receive(byte[] buffer) throws IOException {
            DatagramPacket packet = new DatagramPacket(buffer, buffer.length);
            socket.receive(packet);
            return packet.getLength();
        }

        @Override
        public int localPort() {
            return socket.getLocalPort();
        }

        @Override
        public void close() {
            socket.close();
        }
    }
}
