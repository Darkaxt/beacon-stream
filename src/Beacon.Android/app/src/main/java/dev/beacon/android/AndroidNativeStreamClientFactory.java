package dev.beacon.android;

public final class AndroidNativeStreamClientFactory {
    private AndroidNativeStreamClientFactory() {
    }

    public static NativeStreamClient create(
        EncodedVideoDecoder encodedVideoDecoder,
        GameStreamRtspSessionClient gameStreamRtspSessionClient) {
        return create(encodedVideoDecoder, gameStreamRtspSessionClient, null);
    }

    public static NativeStreamClient create(
        EncodedVideoDecoder encodedVideoDecoder,
        GameStreamRtspSessionClient gameStreamRtspSessionClient,
        GameStreamVideoSessionClient gameStreamVideoSessionClient) {
        return new DiagnosticNativeStreamClient(new NativeStreamClientRouter(
            new EncodedVideoNativeStreamClient(encodedVideoDecoder),
            new BeaconTestNativeStreamClient(),
            new GameStreamNativeStreamClient(gameStreamRtspSessionClient, gameStreamVideoSessionClient)));
    }

    public static GameStreamRtspSessionClient socketRtspSessionClient() {
        return new GameStreamRtspTransportSessionClient(
            new RtspSocketTransportLeaseFactory(),
            () -> GameStreamRtpPortLease.open(new JavaRtpDatagramSocketFactory()),
            GameStreamRtspSdpPayloadProvider.diagnostic());
    }

    public static GameStreamVideoSessionClient socketRtpVideoSessionClient(
        EncodedVideoCodecFactory codecFactory,
        EncodedVideoSurfaceProvider surfaceProvider) {
        return new GameStreamRtpVideoSessionClient(
            new GameStreamUdpRtpPacketSourceFactory(),
            new GameStreamRtpDecoderVideoConsumer(codecFactory, surfaceProvider));
    }
}
