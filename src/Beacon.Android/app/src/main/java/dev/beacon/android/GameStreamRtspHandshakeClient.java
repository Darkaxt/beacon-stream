package dev.beacon.android;

public final class GameStreamRtspHandshakeClient implements GameStreamRtspSessionClient {
    private static final String AudioTarget = "streamid=audio/0/0";
    private static final String VideoTarget = "streamid=video/0/0";
    private static final String ControlTarget = "streamid=control/13/0";
    private static final int AudioClientPort = 50000;
    private static final int VideoClientPort = 50002;
    private static final int ControlClientPort = 50004;

    private final RtspTransport transport;
    private final GameStreamRtspSdpPayloadProvider sdpPayloadProvider;

    public GameStreamRtspHandshakeClient(RtspTransport transport) {
        this(transport, GameStreamRtspSdpPayloadProvider.diagnostic());
    }

    public GameStreamRtspHandshakeClient(
        RtspTransport transport,
        GameStreamRtspSdpPayloadProvider sdpPayloadProvider) {
        this.transport = transport;
        this.sdpPayloadProvider = sdpPayloadProvider == null
            ? GameStreamRtspSdpPayloadProvider.diagnostic()
            : sdpPayloadProvider;
    }

    @Override
    public GameStreamRtspSessionResult start(GameStreamEndpointPlan plan) {
        if (plan == null) {
            return GameStreamRtspSessionResult.failed("GameStream RTSP plan is missing.");
        }

        if (!plan.rtspReady()) {
            return GameStreamRtspSessionResult.failed(plan.rtspDiagnostic());
        }

        if (transport == null) {
            return GameStreamRtspSessionResult.failed(
                "Native GameStream RTSP transport is not configured yet. protocol=" +
                    plan.protocol() +
                    " rtsp=" +
                    plan.rtspUri());
        }

        try {
            return startHandshake(plan);
        } catch (RtspTransportException ex) {
            return GameStreamRtspSessionResult.failed(ex.getMessage());
        }
    }

    private GameStreamRtspSessionResult startHandshake(GameStreamEndpointPlan plan) {
        RtspResponse options = transport.transact(RtspRequest.options(plan.rtspUri(), 1, plan.rtspHostHeader()));
        if (!success(options)) {
            return GameStreamRtspSessionResult.failed(
                "RTSP OPTIONS failed with status " + statusSummary(options) + ".");
        }

        RtspResponse describe = transport.transact(RtspRequest.describe(plan.rtspUri(), 2, plan.rtspHostHeader()));
        if (!success(describe)) {
            return GameStreamRtspSessionResult.failed(
                "RTSP DESCRIBE failed with status " + statusSummary(describe) + ".");
        }

        GameStreamRtspSetupResult audio = setup(plan, AudioTarget, "audio", 3, "", AudioClientPort);
        if (!audio.success()) {
            return GameStreamRtspSessionResult.failed(audio.diagnostic());
        }

        String sessionId = audio.sessionId();
        GameStreamRtspSetupResult video = setup(plan, VideoTarget, "video", 4, sessionId, VideoClientPort);
        if (!video.success()) {
            return GameStreamRtspSessionResult.failed(video.diagnostic());
        }

        GameStreamRtspSetupResult control = setup(plan, ControlTarget, "control", 5, sessionId, ControlClientPort);
        if (!control.success()) {
            return GameStreamRtspSessionResult.failed(control.diagnostic());
        }

        String sdpPayload = sdpPayloadProvider.createSdpPayload(
            plan,
            sessionId,
            audio.serverPort(),
            video.serverPort(),
            control.serverPort());
        if (sdpPayload == null || sdpPayload.trim().isEmpty()) {
            return GameStreamRtspSessionResult.failed("RTSP ANNOUNCE payload is empty.");
        }

        RtspResponse announce = transport.transact(
            RtspRequest.announce(ControlTarget, 6, plan.rtspHostHeader(), sessionId, sdpPayload));
        if (!success(announce)) {
            return GameStreamRtspSessionResult.failed(
                "RTSP ANNOUNCE failed with status " + statusSummary(announce) + ".");
        }

        RtspResponse play = transport.transact(RtspRequest.play("/", 7, plan.rtspHostHeader(), sessionId));
        if (!success(play)) {
            return GameStreamRtspSessionResult.failed(
                "RTSP PLAY failed with status " + statusSummary(play) + ".");
        }

        return GameStreamRtspSessionResult.started(
            "RTSP play started. protocol=" +
                plan.protocol() +
                " rtsp=" +
                plan.rtspUri() +
                " session=" +
                sessionId +
                " audioPort=" +
                audio.serverPort() +
                " videoPort=" +
                video.serverPort() +
                " controlPort=" +
                control.serverPort(),
            GameStreamRtspSessionInfo.startedWithClientPorts(
                plan.protocol(),
                plan.rtspUri(),
                sessionId,
                AudioClientPort,
                audio.serverPort(),
                VideoClientPort,
                video.serverPort(),
                ControlClientPort,
                control.serverPort()));
    }

    private GameStreamRtspSetupResult setup(
        GameStreamEndpointPlan plan,
        String target,
        String streamName,
        int cseq,
        String sessionId,
        int clientRtpPort) {
        RtspResponse response = transport.transact(
            RtspRequest.setup(target, cseq, plan.rtspHostHeader(), sessionId, clientRtpPort));
        if (!success(response)) {
            return GameStreamRtspSetupResult.failedWithDiagnostic(
                "RTSP SETUP " + streamName + " failed with status " + statusSummary(response) + ".");
        }

        return GameStreamRtspSetupResult.fromResponse(streamName, response);
    }

    private static boolean success(RtspResponse response) {
        return response != null && response.statusCode() >= 200 && response.statusCode() < 300;
    }

    private static String statusSummary(RtspResponse response) {
        if (response == null) {
            return "no response";
        }

        String reason = response.reasonPhrase();
        if (reason.isEmpty()) {
            return Integer.toString(response.statusCode());
        }

        return response.statusCode() + " " + reason;
    }
}
