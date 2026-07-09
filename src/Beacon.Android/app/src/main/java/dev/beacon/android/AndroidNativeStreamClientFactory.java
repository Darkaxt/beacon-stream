package dev.beacon.android;

public final class AndroidNativeStreamClientFactory {
    private AndroidNativeStreamClientFactory() {
    }

    public static NativeStreamClient create(
        EncodedVideoDecoder encodedVideoDecoder,
        GameStreamRtspSessionClient gameStreamRtspSessionClient) {
        return new DiagnosticNativeStreamClient(new NativeStreamClientRouter(
            new EncodedVideoNativeStreamClient(encodedVideoDecoder),
            new BeaconTestNativeStreamClient(),
            new GameStreamNativeStreamClient(gameStreamRtspSessionClient)));
    }

    public static GameStreamRtspSessionClient socketRtspSessionClient() {
        return new GameStreamRtspTransportSessionClient(
            new RtspSocketTransportLeaseFactory(),
            GameStreamRtspSdpPayloadProvider.diagnostic());
    }
}
