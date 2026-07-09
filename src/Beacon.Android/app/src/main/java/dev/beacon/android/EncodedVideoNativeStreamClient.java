package dev.beacon.android;

public final class EncodedVideoNativeStreamClient implements NativeStreamProtocolClient {
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

        return NativeStreamStartResult.unsupported(
            "Beacon encoded video contract is valid, but MediaCodec decode is not implemented yet. codec=" +
                plan.codec() +
                " container=" + plan.container() +
                " video=" + plan.videoUri() +
                " " + plan.width() + "x" + plan.height() + "@" + plan.fps());
    }

    @Override
    public void stop() {
    }
}
