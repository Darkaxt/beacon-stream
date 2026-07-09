package dev.beacon.android;

import java.nio.charset.StandardCharsets;
import java.util.LinkedHashMap;
import java.util.Map;

public final class RtspRequest {
    private static final String ClientVersion = "BeaconStream";

    private final String method;
    private final String uri;
    private final int cseq;
    private final String host;
    private final Map<String, String> headers;
    private final String payload;

    private RtspRequest(String method, String uri, int cseq, String host, Map<String, String> headers) {
        this(method, uri, cseq, host, headers, "");
    }

    private RtspRequest(String method, String uri, int cseq, String host, Map<String, String> headers, String payload) {
        this.method = method == null ? "" : method;
        this.uri = uri == null ? "" : uri;
        this.cseq = cseq;
        this.host = host == null ? "" : host;
        this.headers = new LinkedHashMap<>(headers);
        this.payload = payload == null ? "" : payload;
    }

    public static RtspRequest options(String uri, int cseq, String host) {
        return new RtspRequest("OPTIONS", uri, cseq, host, new LinkedHashMap<>());
    }

    public static RtspRequest describe(String uri, int cseq, String host) {
        Map<String, String> headers = new LinkedHashMap<>();
        headers.put("Accept", "application/sdp");
        headers.put("If-Modified-Since", "Thu, 01 Jan 1970 00:00:00 GMT");
        return new RtspRequest("DESCRIBE", uri, cseq, host, headers);
    }

    public static RtspRequest setup(String target, int cseq, String host, String sessionId) {
        Map<String, String> headers = new LinkedHashMap<>();
        String session = sessionId == null ? "" : sessionId.trim();
        if (!session.isEmpty()) {
            headers.put("Session", session);
        }

        headers.put("Transport", "unicast;X-GS-ClientPort=50000-50001");
        headers.put("If-Modified-Since", "Thu, 01 Jan 1970 00:00:00 GMT");
        return new RtspRequest("SETUP", target, cseq, host, headers);
    }

    public static RtspRequest announce(String target, int cseq, String host, String sessionId, String payload) {
        String body = payload == null ? "" : payload;
        Map<String, String> headers = new LinkedHashMap<>();
        headers.put("Session", sessionId == null ? "" : sessionId.trim());
        headers.put("Content-type", "application/sdp");
        headers.put("Content-length", Integer.toString(body.getBytes(StandardCharsets.UTF_8).length));
        return new RtspRequest("ANNOUNCE", target, cseq, host, headers, body);
    }

    public static RtspRequest play(String target, int cseq, String host, String sessionId) {
        Map<String, String> headers = new LinkedHashMap<>();
        headers.put("Session", sessionId == null ? "" : sessionId.trim());
        return new RtspRequest("PLAY", target, cseq, host, headers);
    }

    public String serialize() {
        StringBuilder builder = new StringBuilder();
        builder.append(method).append(' ').append(uri).append(" RTSP/1.0\r\n");
        builder.append("CSeq: ").append(cseq).append("\r\n");
        builder.append("Host: ").append(host).append("\r\n");
        builder.append("X-GS-ClientVersion: ").append(ClientVersion).append("\r\n");
        for (Map.Entry<String, String> header : headers.entrySet()) {
            builder.append(header.getKey()).append(": ").append(header.getValue()).append("\r\n");
        }

        builder.append("\r\n");
        if (!payload.isEmpty()) {
            builder.append(payload);
        }

        return builder.toString();
    }
}
