package dev.beacon.android;

import java.io.IOException;

public final class GameStreamUdpRtpPacketSourceFactory implements GameStreamRtpPacketSourceFactory {
    private final RtpDatagramSocketFactory socketFactory;

    public GameStreamUdpRtpPacketSourceFactory() {
        this(new JavaRtpDatagramSocketFactory());
    }

    public GameStreamUdpRtpPacketSourceFactory(RtpDatagramSocketFactory socketFactory) {
        if (socketFactory == null) {
            throw new IllegalArgumentException("RTP datagram socket factory is required.");
        }

        this.socketFactory = socketFactory;
    }

    @Override
    public RtpPacketSource create(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo) {
        if (sessionInfo == null || !sessionInfo.present() || sessionInfo.videoClientPort() <= 0) {
            throw new IllegalStateException("GameStream RTP video client port is unavailable.");
        }

        GameStreamRtpPortLease rtpPortLease = sessionInfo.rtpPortLease();
        if (rtpPortLease != null) {
            RtpDatagramSocket leasedSocket = rtpPortLease.takeVideoSocket();
            if (leasedSocket == null) {
                throw new IllegalStateException("GameStream RTP video socket lease is unavailable.");
            }

            return new RtpDatagramPacketSource(leasedSocket);
        }

        RtpDatagramSocket socket;
        try {
            socket = socketFactory.bind(sessionInfo.videoClientPort());
        } catch (IOException ex) {
            throw new IllegalStateException("RTP UDP socket open failed: " + safeMessage(ex), ex);
        }

        if (socket == null) {
            throw new IllegalStateException("RTP UDP socket factory returned no socket.");
        }

        return new RtpDatagramPacketSource(socket);
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isEmpty() ? throwable.getClass().getSimpleName() : message;
    }
}
