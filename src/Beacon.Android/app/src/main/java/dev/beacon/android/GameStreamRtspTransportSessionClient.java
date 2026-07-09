package dev.beacon.android;

public final class GameStreamRtspTransportSessionClient implements GameStreamRtspSessionClient {
    private final RtspTransportLeaseFactory leaseFactory;
    private final GameStreamRtspSdpPayloadProvider sdpPayloadProvider;
    private RtspTransportLease activeLease;

    public GameStreamRtspTransportSessionClient(
        RtspTransportLeaseFactory leaseFactory,
        GameStreamRtspSdpPayloadProvider sdpPayloadProvider) {
        this.leaseFactory = leaseFactory;
        this.sdpPayloadProvider = sdpPayloadProvider == null
            ? GameStreamRtspSdpPayloadProvider.diagnostic()
            : sdpPayloadProvider;
    }

    @Override
    public synchronized GameStreamRtspSessionResult start(GameStreamEndpointPlan plan) {
        closeActiveLease();

        RtspTransportLease lease;
        try {
            lease = openLease(plan);
        } catch (RtspTransportException ex) {
            return GameStreamRtspSessionResult.failed(ex.getMessage());
        } catch (RuntimeException ex) {
            return GameStreamRtspSessionResult.failed(
                "RTSP transport factory failed: " + safeMessage(ex));
        }

        RtspTransport transport = lease.transport();
        if (transport == null) {
            closeLeaseQuietly(lease);
            return GameStreamRtspSessionResult.failed("RTSP transport factory did not provide a transport.");
        }

        GameStreamRtspSessionResult result = new GameStreamRtspHandshakeClient(
            transport,
            sdpPayloadProvider).start(plan);
        if (result.success()) {
            activeLease = lease;
        } else {
            closeLeaseQuietly(lease);
        }

        return result;
    }

    @Override
    public synchronized void stop() {
        closeActiveLease();
    }

    private RtspTransportLease openLease(GameStreamEndpointPlan plan) {
        if (leaseFactory == null) {
            throw new RtspTransportException("RTSP transport factory is not configured.");
        }

        RtspTransportLease lease = leaseFactory.open(plan);
        if (lease == null) {
            throw new RtspTransportException("RTSP transport factory did not provide a lease.");
        }

        return lease;
    }

    private void closeActiveLease() {
        RtspTransportLease lease = activeLease;
        activeLease = null;
        closeLeaseQuietly(lease);
    }

    private static void closeLeaseQuietly(RtspTransportLease lease) {
        if (lease == null) {
            return;
        }

        try {
            lease.close();
        } catch (RuntimeException ignored) {
        }
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isEmpty() ? throwable.getClass().getSimpleName() : message;
    }
}
