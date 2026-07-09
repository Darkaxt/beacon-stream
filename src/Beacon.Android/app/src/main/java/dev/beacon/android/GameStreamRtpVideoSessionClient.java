package dev.beacon.android;

public final class GameStreamRtpVideoSessionClient implements GameStreamVideoSessionClient {
    private final GameStreamRtpPacketSourceFactory sourceFactory;
    private final GameStreamRtpVideoConsumer consumer;
    private RtpPacketSource activeSource;

    public GameStreamRtpVideoSessionClient(
        GameStreamRtpPacketSourceFactory sourceFactory,
        GameStreamRtpVideoConsumer consumer) {
        if (sourceFactory == null) {
            throw new IllegalArgumentException("GameStream RTP packet source factory is required.");
        }

        if (consumer == null) {
            throw new IllegalArgumentException("GameStream RTP video consumer is required.");
        }

        this.sourceFactory = sourceFactory;
        this.consumer = consumer;
    }

    @Override
    public synchronized NativeStreamStartResult start(
        GameStreamEndpointPlan plan,
        GameStreamRtspSessionInfo sessionInfo) {
        closeActiveSource();

        RtpPacketSource source;
        try {
            source = sourceFactory.create(plan, sessionInfo);
        } catch (RuntimeException ex) {
            return NativeStreamStartResult.unsupported(
                "GameStream RTP video source failed: " + safeMessage(ex));
        }

        if (source == null) {
            return NativeStreamStartResult.unsupported("GameStream RTP video source factory returned no source.");
        }

        NativeStreamStartResult result;
        try {
            result = consumer.start(
                plan,
                sessionInfo,
                sampleProvider(plan, source));
        } catch (RuntimeException ex) {
            closeSourceQuietly(source);
            return NativeStreamStartResult.unsupported(
                "GameStream RTP video consumer failed: " + safeMessage(ex));
        }

        if (result.success()) {
            activeSource = source;
        } else {
            closeSourceQuietly(source);
        }

        return result;
    }

    @Override
    public synchronized void stop() {
        closeActiveSource();
    }

    private void closeActiveSource() {
        RtpPacketSource source = activeSource;
        activeSource = null;
        if (source != null) {
            stopConsumerQuietly();
        }

        closeSourceQuietly(source);
    }

    private void stopConsumerQuietly() {
        try {
            consumer.stop();
        } catch (RuntimeException ignored) {
        }
    }

    private static EncodedVideoSampleProvider sampleProvider(GameStreamEndpointPlan plan, RtpPacketSource source) {
        if (plan != null && "h264".equalsIgnoreCase(plan.metadataValue("codec"))) {
            return new H264RtpSampleProvider(source);
        }

        return new GameStreamRtpVideoSampleProvider(source);
    }

    private static void closeSourceQuietly(RtpPacketSource source) {
        if (source == null) {
            return;
        }

        try {
            source.close();
        } catch (RuntimeException ignored) {
        }
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isEmpty() ? throwable.getClass().getSimpleName() : message;
    }
}
