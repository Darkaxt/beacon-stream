package dev.beacon.android;

public final class GameStreamRtspSetupResult {
    private final boolean success;
    private final String sessionId;
    private final int serverPort;
    private final String diagnostic;

    private GameStreamRtspSetupResult(boolean success, String sessionId, int serverPort, String diagnostic) {
        this.success = success;
        this.sessionId = sessionId == null ? "" : sessionId;
        this.serverPort = serverPort;
        this.diagnostic = diagnostic == null ? "" : diagnostic;
    }

    public static GameStreamRtspSetupResult failedWithDiagnostic(String diagnostic) {
        return new GameStreamRtspSetupResult(false, "", -1, diagnostic);
    }

    public static GameStreamRtspSetupResult fromResponse(String streamName, RtspResponse response) {
        String name = streamName == null || streamName.trim().isEmpty() ? "stream" : streamName.trim();
        if (response == null) {
            return failed(name);
        }

        int serverPort = parseServerPort(response.header("transport"));
        if (serverPort <= 0) {
            return failed(name);
        }

        return new GameStreamRtspSetupResult(true, parseSessionId(response.header("session")), serverPort, "");
    }

    public boolean success() {
        return success;
    }

    public String sessionId() {
        return sessionId;
    }

    public int serverPort() {
        return serverPort;
    }

    public String diagnostic() {
        return diagnostic;
    }

    private static GameStreamRtspSetupResult failed(String streamName) {
        return new GameStreamRtspSetupResult(
            false,
            "",
            -1,
            "RTSP SETUP " + streamName + " response did not include a valid server_port in the Transport header.");
    }

    private static String parseSessionId(String rawSession) {
        if (rawSession == null || rawSession.trim().isEmpty()) {
            return "";
        }

        String session = rawSession.trim();
        int parameterStart = session.indexOf(';');
        if (parameterStart >= 0) {
            session = session.substring(0, parameterStart);
        }

        return session.trim();
    }

    private static int parseServerPort(String transport) {
        if (transport == null || transport.trim().isEmpty()) {
            return -1;
        }

        String marker = "server_port=";
        int markerIndex = transport.indexOf(marker);
        if (markerIndex < 0) {
            return -1;
        }

        int portStart = markerIndex + marker.length();
        int portEnd = portStart;
        while (portEnd < transport.length() && Character.isDigit(transport.charAt(portEnd))) {
            portEnd++;
        }

        if (portEnd == portStart) {
            return -1;
        }

        try {
            int port = Integer.parseInt(transport.substring(portStart, portEnd));
            if (port <= 0 || port > 65535) {
                return -1;
            }

            return port;
        } catch (NumberFormatException ex) {
            return -1;
        }
    }
}
