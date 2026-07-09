package dev.beacon.android;

public final class DiagnosticNativeStreamClient implements NativeStreamClient {
    private final NativeStreamClientRouter router;

    public DiagnosticNativeStreamClient() {
        this(new NativeStreamClientRouter(
            new EncodedVideoNativeStreamClient(),
            new BeaconTestNativeStreamClient(),
            new GameStreamNativeStreamClient()));
    }

    DiagnosticNativeStreamClient(NativeStreamClientRouter router) {
        this.router = router;
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        return router.start(connection);
    }

    @Override
    public void stop() {
        router.stop();
    }
}
