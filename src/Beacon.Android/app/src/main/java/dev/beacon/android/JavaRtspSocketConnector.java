package dev.beacon.android;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Socket;

public final class JavaRtspSocketConnector implements RtspSocketConnector {
    @Override
    public RtspSocketHandle open(String host, int port) throws IOException {
        Socket socket = new Socket(host, port);
        return new JavaRtspSocketHandle(socket);
    }

    private static final class JavaRtspSocketHandle implements RtspSocketHandle {
        private final Socket socket;

        private JavaRtspSocketHandle(Socket socket) {
            this.socket = socket;
        }

        @Override
        public InputStream inputStream() throws IOException {
            return socket.getInputStream();
        }

        @Override
        public OutputStream outputStream() throws IOException {
            return socket.getOutputStream();
        }

        @Override
        public void close() throws IOException {
            socket.close();
        }
    }
}
