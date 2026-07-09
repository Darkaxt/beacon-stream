package dev.beacon.android;

public final class GameStreamRtspTransportSessionClient implements GameStreamRtspSessionClient {
    private final RtspTransportLeaseFactory leaseFactory;
    private final GameStreamRtpPortLeaseFactory rtpPortLeaseFactory;
    private final GameStreamRtspSdpPayloadProvider sdpPayloadProvider;
    private RtspTransportLease activeLease;
    private GameStreamRtpPortLease activeRtpPortLease;

    public GameStreamRtspTransportSessionClient(
        RtspTransportLeaseFactory leaseFactory,
        GameStreamRtspSdpPayloadProvider sdpPayloadProvider) {
        this(
            leaseFactory,
            () -> GameStreamRtpPortLease.staticPorts(50000, 50002, 50004),
            sdpPayloadProvider);
    }

    public GameStreamRtspTransportSessionClient(
        RtspTransportLeaseFactory leaseFactory,
        GameStreamRtpPortLeaseFactory rtpPortLeaseFactory,
        GameStreamRtspSdpPayloadProvider sdpPayloadProvider) {
        this.leaseFactory = leaseFactory;
        this.rtpPortLeaseFactory = rtpPortLeaseFactory;
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

        GameStreamRtpPortLease rtpPortLease;
        try {
            rtpPortLease = openRtpPortLease();
        } catch (RuntimeException ex) {
            closeLeaseQuietly(lease);
            return GameStreamRtspSessionResult.failed(safeMessage(ex));
        }

        RtspTransport transport = lease.transport();
        if (transport == null) {
            closeLeaseQuietly(lease);
            closeRtpPortLeaseQuietly(rtpPortLease);
            return GameStreamRtspSessionResult.failed("RTSP transport factory did not provide a transport.");
        }

        GameStreamRtspSessionResult result = new GameStreamRtspHandshakeClient(
            transport,
            sdpPayloadProvider,
            rtpPortLease).start(plan);
        if (result.success()) {
            activeLease = lease;
            activeRtpPortLease = rtpPortLease;
        } else {
            closeLeaseQuietly(lease);
            closeRtpPortLeaseQuietly(rtpPortLease);
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

    private GameStreamRtpPortLease openRtpPortLease() {
        if (rtpPortLeaseFactory == null) {
            throw new IllegalStateException("RTP port lease factory is not configured.");
        }

        GameStreamRtpPortLease lease = rtpPortLeaseFactory.open();
        if (lease == null) {
            throw new IllegalStateException("RTP port lease factory did not provide a lease.");
        }

        return lease;
    }

    private void closeActiveLease() {
        RtspTransportLease lease = activeLease;
        GameStreamRtpPortLease rtpPortLease = activeRtpPortLease;
        activeLease = null;
        activeRtpPortLease = null;
        closeLeaseQuietly(lease);
        closeRtpPortLeaseQuietly(rtpPortLease);
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

    private static void closeRtpPortLeaseQuietly(GameStreamRtpPortLease lease) {
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
