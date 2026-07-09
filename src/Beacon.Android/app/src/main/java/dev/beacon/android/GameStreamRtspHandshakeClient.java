package dev.beacon.android;

public final class GameStreamRtspHandshakeClient implements GameStreamRtspSessionClient {
    private final RtspTransport transport;

    public GameStreamRtspHandshakeClient(RtspTransport transport) {
        this.transport = transport;
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

        return GameStreamRtspSessionResult.started(
            "RTSP handshake completed. protocol=" + plan.protocol() + " rtsp=" + plan.rtspUri());
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
