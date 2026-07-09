package dev.beacon.android;

public final class GameStreamNativeStreamClient implements NativeStreamProtocolClient {
    private final GameStreamRtspSessionClient rtspSessionClient;
    private boolean rtspSessionActive;

    public GameStreamNativeStreamClient() {
        this(GameStreamRtspSessionClient.notConfigured());
    }

    public GameStreamNativeStreamClient(GameStreamRtspSessionClient rtspSessionClient) {
        this.rtspSessionClient = rtspSessionClient == null ? GameStreamRtspSessionClient.notConfigured() : rtspSessionClient;
    }

    @Override
    public boolean supports(StreamConnectionDescriptor connection) {
        return GameStreamEndpointPlan.from(connection).supportedProtocol();
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connection) {
        GameStreamEndpointPlan gameStreamPlan = GameStreamEndpointPlan.from(connection);
        if (gameStreamPlan.supportedProtocol() && connection.launchUri().isEmpty()) {
            String endpointSummary = gameStreamPlan.diagnosticEndpointSummary();
            if (!gameStreamPlan.complete()) {
                return NativeStreamStartResult.unsupported(
                    "GameStream endpoint map is incomplete. Missing required endpoints: " +
                        gameStreamPlan.missingRequiredRoles() +
                        ". protocol=" + gameStreamPlan.protocol() +
                        " endpoints=" + endpointSummary);
            }

            if (!gameStreamPlan.rtspReady()) {
                return NativeStreamStartResult.unsupported(gameStreamPlan.rtspDiagnostic());
            }

            GameStreamRtspSessionResult rtspResult = rtspSessionClient.start(gameStreamPlan);
            rtspSessionActive = rtspResult.success();
            if (!rtspResult.success()) {
                return NativeStreamStartResult.unsupported(rtspResult.diagnostic());
            }

            return NativeStreamStartResult.started(
                "Native GameStream RTSP session started. protocol=" +
                    gameStreamPlan.protocol() +
                    " rtsp=" +
                    gameStreamPlan.rtspUri());
        }

        return NativeStreamStartResult.unsupported(connection.missingLaunchUriDiagnostic());
    }

    @Override
    public void stop() {
        if (!rtspSessionActive) {
            return;
        }

        rtspSessionActive = false;
        rtspSessionClient.stop();
    }
}
