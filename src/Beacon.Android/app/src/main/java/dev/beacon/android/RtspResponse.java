package dev.beacon.android;

import java.util.LinkedHashMap;
import java.util.Locale;
import java.util.Map;

public final class RtspResponse {
    private final int statusCode;
    private final String reasonPhrase;
    private final Map<String, String> headers;

    private RtspResponse(int statusCode, String reasonPhrase, Map<String, String> headers) {
        this.statusCode = statusCode;
        this.reasonPhrase = reasonPhrase == null ? "" : reasonPhrase;
        this.headers = new LinkedHashMap<>(headers);
    }

    public static RtspResponse parse(String rawResponse) {
        String response = rawResponse == null ? "" : rawResponse;
        String[] lines = response.split("\\r?\\n");
        String statusLine = lines.length == 0 ? "" : lines[0].trim();
        String[] parts = statusLine.split(" ", 3);
        if (parts.length < 2 || !"RTSP/1.0".equals(parts[0])) {
            throw new IllegalArgumentException("Invalid RTSP status line: " + statusLine);
        }

        int statusCode;
        try {
            statusCode = Integer.parseInt(parts[1]);
        } catch (NumberFormatException ex) {
            throw new IllegalArgumentException("Invalid RTSP status line: " + statusLine, ex);
        }

        String reasonPhrase = parts.length == 3 ? parts[2].trim() : "";
        Map<String, String> headers = new LinkedHashMap<>();
        for (int index = 1; index < lines.length; index++) {
            String line = lines[index];
            if (line == null || line.isEmpty()) {
                break;
            }

            int separator = line.indexOf(':');
            if (separator <= 0) {
                continue;
            }

            String name = line.substring(0, separator).trim().toLowerCase(Locale.ROOT);
            String value = line.substring(separator + 1).trim();
            if (!name.isEmpty()) {
                headers.put(name, value);
            }
        }

        return new RtspResponse(statusCode, reasonPhrase, headers);
    }

    public int statusCode() {
        return statusCode;
    }

    public String reasonPhrase() {
        return reasonPhrase;
    }

    public String header(String name) {
        if (name == null || name.trim().isEmpty()) {
            return "";
        }

        return headers.getOrDefault(name.trim().toLowerCase(Locale.ROOT), "");
    }
}
