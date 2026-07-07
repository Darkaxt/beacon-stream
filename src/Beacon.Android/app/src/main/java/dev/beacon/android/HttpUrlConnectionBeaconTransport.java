package dev.beacon.android;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.HttpURLConnection;
import java.net.URL;
import java.nio.charset.StandardCharsets;

public final class HttpUrlConnectionBeaconTransport implements BeaconHttpTransport {
    private final BeaconClientConfig config;

    public HttpUrlConnectionBeaconTransport(BeaconClientConfig config) {
        this.config = config;
    }

    @Override
    public BeaconHttpResponse send(String method, String path, String body) throws IOException {
        URL url = new URL(config.serverUrl() + path);
        HttpURLConnection connection = (HttpURLConnection) url.openConnection();
        connection.setRequestMethod(method);
        connection.setRequestProperty("Accept", "application/json");

        if (body != null) {
            byte[] payload = body.getBytes(StandardCharsets.UTF_8);
            connection.setDoOutput(true);
            connection.setRequestProperty("Content-Type", "application/json; charset=utf-8");
            connection.setFixedLengthStreamingMode(payload.length);
            try (OutputStream stream = connection.getOutputStream()) {
                stream.write(payload);
            }
        }

        int statusCode = connection.getResponseCode();
        InputStream responseStream = statusCode >= 400 ? connection.getErrorStream() : connection.getInputStream();
        String responseBody = responseStream == null ? "" : readAll(responseStream);
        connection.disconnect();
        return new BeaconHttpResponse(statusCode, responseBody);
    }

    private static String readAll(InputStream stream) throws IOException {
        try (InputStream input = stream; ByteArrayOutputStream output = new ByteArrayOutputStream()) {
            byte[] buffer = new byte[4096];
            int read;
            while ((read = input.read(buffer)) >= 0) {
                output.write(buffer, 0, read);
            }

            return output.toString(StandardCharsets.UTF_8.name());
        }
    }
}
