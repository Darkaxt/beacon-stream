package dev.beacon.android;

import java.io.IOException;

public final class RtspSocketTransportLeaseFactory implements RtspTransportLeaseFactory {
    private final RtspSocketConnector connector;

    public RtspSocketTransportLeaseFactory() {
        this(new JavaRtspSocketConnector());
    }

    public RtspSocketTransportLeaseFactory(RtspSocketConnector connector) {
        this.connector = connector;
    }

    @Override
    public RtspTransportLease open(GameStreamEndpointPlan plan) {
        if (plan == null) {
            throw new RtspTransportException("GameStream RTSP plan is missing.");
        }

        if (!plan.rtspReady()) {
            throw new RtspTransportException(plan.rtspDiagnostic());
        }

        if (connector == null) {
            throw new RtspTransportException("RTSP socket connector is not configured.");
        }

        RtspSocketHandle handle = null;
        try {
            handle = connector.open(plan.rtspHost(), plan.rtspPort());
            if (handle == null) {
                throw new IOException("connector returned no socket handle");
            }

            RtspByteStreamTransport transport = new RtspByteStreamTransport(
                handle.inputStream(),
                handle.outputStream());
            return new SocketRtspTransportLease(transport, handle);
        } catch (IOException ex) {
            closeHandleQuietly(handle);
            throw new RtspTransportException("RTSP socket open failed: " + safeMessage(ex), ex);
        } catch (RtspTransportException ex) {
            closeHandleQuietly(handle);
            throw ex;
        }
    }

    private static void closeHandleQuietly(RtspSocketHandle handle) {
        if (handle == null) {
            return;
        }

        try {
            handle.close();
        } catch (IOException ignored) {
        }
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isEmpty() ? throwable.getClass().getSimpleName() : message;
    }

    private static final class SocketRtspTransportLease implements RtspTransportLease {
        private final RtspByteStreamTransport transport;
        private final RtspSocketHandle handle;
        private boolean closed;

        private SocketRtspTransportLease(RtspByteStreamTransport transport, RtspSocketHandle handle) {
            this.transport = transport;
            this.handle = handle;
        }

        @Override
        public RtspTransport transport() {
            return transport;
        }

        @Override
        public void close() {
            if (closed) {
                return;
            }

            closed = true;
            RtspTransportException failure = null;
            try {
                transport.close();
            } catch (RtspTransportException ex) {
                failure = ex;
            }

            try {
                handle.close();
            } catch (IOException ex) {
                if (failure == null) {
                    failure = new RtspTransportException("RTSP socket close failed: " + safeMessage(ex), ex);
                }
            }

            if (failure != null) {
                throw failure;
            }
        }
    }
}
