package dev.beacon.android;

public final class GameStreamNativeStreamClient implements NativeStreamProtocolClient {
    private final GameStreamRtspSessionClient rtspSessionClient;
    private final GameStreamVideoSessionClient videoSessionClient;
    private boolean rtspSessionActive;
    private boolean videoSessionActive;

    public GameStreamNativeStreamClient() {
        this(GameStreamRtspSessionClient.notConfigured());
    }

    public GameStreamNativeStreamClient(GameStreamRtspSessionClient rtspSessionClient) {
        this(rtspSessionClient, null);
    }

    public GameStreamNativeStreamClient(
        GameStreamRtspSessionClient rtspSessionClient,
        GameStreamVideoSessionClient videoSessionClient) {
        this.rtspSessionClient = rtspSessionClient == null ? GameStreamRtspSessionClient.notConfigured() : rtspSessionClient;
        this.videoSessionClient = videoSessionClient;
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

            if (videoSessionClient != null) {
                return startVideoSession(gameStreamPlan, rtspResult.sessionInfo());
            }

            return NativeStreamStartResult.started(
                "Native GameStream RTSP session started. protocol=" +
                    gameStreamPlan.protocol() +
                    " rtsp=" +
                    gameStreamPlan.rtspUri());
        }

        return NativeStreamStartResult.unsupported(connection.missingLaunchUriDiagnostic());
    }

    private NativeStreamStartResult startVideoSession(
        GameStreamEndpointPlan gameStreamPlan,
        GameStreamRtspSessionInfo sessionInfo) {
        if (sessionInfo == null || !sessionInfo.present()) {
            stopRtspSession();
            return NativeStreamStartResult.unsupported("Native GameStream RTSP session did not include media setup info.");
        }

        NativeStreamStartResult videoResult = videoSessionClient.start(gameStreamPlan, sessionInfo);
        if (!videoResult.success()) {
            stopRtspSession();
            return videoResult;
        }

        videoSessionActive = true;
        return videoResult;
    }

    @Override
    public void stop() {
        if (videoSessionActive) {
            videoSessionActive = false;
            videoSessionClient.stop();
        }

        stopRtspSession();
    }

    private void stopRtspSession() {
        if (!rtspSessionActive) {
            return;
        }

        rtspSessionActive = false;
        rtspSessionClient.stop();
    }
}
