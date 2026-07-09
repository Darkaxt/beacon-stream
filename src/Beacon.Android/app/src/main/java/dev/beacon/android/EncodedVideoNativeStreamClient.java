package dev.beacon.android;

public final class EncodedVideoNativeStreamClient implements NativeStreamProtocolClient {
    private final EncodedVideoDecoder decoder;
    private boolean decoderStarted;

    public EncodedVideoNativeStreamClient() {
        this(new NotConfiguredEncodedVideoDecoder());
    }

    EncodedVideoNativeStreamClient(EncodedVideoDecoder decoder) {
        if (decoder == null) {
            throw new IllegalArgumentException("Encoded video decoder is required.");
        }

        this.decoder = decoder;
    }

    @Override
    public boolean supports(StreamConnectionDescriptor connection) {
        return EncodedVideoStreamPlan.from(connection).supportedProtocol();
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        EncodedVideoStreamPlan plan = EncodedVideoStreamPlan.from(connection);
        if (!plan.complete()) {
            return NativeStreamStartResult.unsupported(plan.diagnostic());
        }

        EncodedVideoDecodeResult decoded = decoder.start(new EncodedVideoDecodeRequest(plan));
        if (!decoded.success()) {
            return NativeStreamStartResult.unsupported(decoded.diagnostic());
        }

        decoderStarted = true;
        return NativeStreamStartResult.started(
            "Native encoded video stream started. codec=" +
                plan.codec() +
                " container=" + plan.container() +
                " video=" + plan.videoUri() +
                " " + plan.width() + "x" + plan.height() + "@" + plan.fps(),
            NativeStreamPresentation.encodedVideo(
                plan.videoUri(),
                plan.codec(),
                plan.width(),
                plan.height(),
                plan.fps()));
    }

    @Override
    public void stop() {
        if (!decoderStarted) {
            return;
        }

        decoder.stop();
        decoderStarted = false;
    }

    private static final class NotConfiguredEncodedVideoDecoder implements EncodedVideoDecoder {
        @Override
        public EncodedVideoDecodeResult start(EncodedVideoDecodeRequest request) {
            EncodedVideoStreamPlan plan = request.plan();
            return EncodedVideoDecodeResult.failed(
                "Beacon encoded video contract is valid, but no MediaCodec decoder is configured yet. codec=" +
                    plan.codec() +
                    " container=" + plan.container() +
                    " video=" + plan.videoUri() +
                    " " + plan.width() + "x" + plan.height() + "@" + plan.fps());
        }

        @Override
        public void stop() {
        }
    }
}
