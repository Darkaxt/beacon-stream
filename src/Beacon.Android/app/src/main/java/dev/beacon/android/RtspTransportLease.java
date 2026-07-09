package dev.beacon.android;

public interface RtspTransportLease extends AutoCloseable {
    RtspTransport transport();

    @Override
    void close();

    static RtspTransportLease of(RtspTransport transport, Runnable closeAction) {
        return new RtspTransportLease() {
            private boolean closed;

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
                if (closeAction != null) {
                    closeAction.run();
                }
            }
        };
    }
}
