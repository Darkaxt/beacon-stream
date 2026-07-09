package dev.beacon.android;

import java.io.ByteArrayOutputStream;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.nio.charset.StandardCharsets;

public final class RtspByteStreamTransport implements RtspTransport, AutoCloseable {
    private final InputStream input;
    private final OutputStream output;
    private boolean closed;

    public RtspByteStreamTransport(InputStream input, OutputStream output) {
        this.input = input;
        this.output = output;
    }

    @Override
    public RtspResponse transact(RtspRequest request) {
        if (closed) {
            throw new RtspTransportException("RTSP transport is closed.");
        }

        writeRequest(request);
        String rawHeaders = readResponseHeaders();
        RtspResponse response = parseResponse(rawHeaders);
        return response.withBody(readBody(response));
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }

        closed = true;
        IOException failure = null;
        if (input != null) {
            try {
                input.close();
            } catch (IOException ex) {
                failure = ex;
            }
        }

        if (output != null) {
            try {
                output.close();
            } catch (IOException ex) {
                if (failure == null) {
                    failure = ex;
                }
            }
        }

        if (failure != null) {
            throw new RtspTransportException("RTSP close failed: " + safeMessage(failure), failure);
        }
    }

    private void writeRequest(RtspRequest request) {
        if (output == null) {
            throw new RtspTransportException("RTSP output stream is missing.");
        }

        String serialized = request == null ? "" : request.serialize();
        byte[] bytes = serialized.getBytes(StandardCharsets.UTF_8);
        try {
            output.write(bytes);
            output.flush();
        } catch (IOException ex) {
            throw new RtspTransportException("RTSP write failed: " + safeMessage(ex), ex);
        }
    }

    private String readResponseHeaders() {
        if (input == null) {
            throw new RtspTransportException("RTSP input stream is missing.");
        }

        ByteArrayOutputStream buffer = new ByteArrayOutputStream();
        try {
            int current;
            int fourthPrevious = -1;
            int thirdPrevious = -1;
            int secondPrevious = -1;
            int previous = -1;
            while ((current = input.read()) >= 0) {
                buffer.write(current);
                fourthPrevious = thirdPrevious;
                thirdPrevious = secondPrevious;
                secondPrevious = previous;
                previous = current;
                if (fourthPrevious == '\r' &&
                    thirdPrevious == '\n' &&
                    secondPrevious == '\r' &&
                    previous == '\n') {
                    return buffer.toString(StandardCharsets.UTF_8.name());
                }
            }
        } catch (IOException ex) {
            throw new RtspTransportException("RTSP read failed: " + safeMessage(ex), ex);
        }

        if (buffer.size() == 0) {
            throw new RtspTransportException("RTSP response ended before a status line was read.");
        }

        throw new RtspTransportException("RTSP response ended before headers completed.");
    }

    private RtspResponse parseResponse(String rawHeaders) {
        try {
            return RtspResponse.parse(rawHeaders);
        } catch (IllegalArgumentException ex) {
            throw new RtspTransportException("RTSP response could not be parsed: " + safeMessage(ex), ex);
        }
    }

    private String readBody(RtspResponse response) {
        int contentLength = parseContentLength(response.header("Content-Length"));
        if (contentLength <= 0) {
            return "";
        }

        byte[] buffer = new byte[Math.min(4096, contentLength)];
        ByteArrayOutputStream body = new ByteArrayOutputStream(contentLength);
        int remaining = contentLength;
        try {
            while (remaining > 0) {
                int read = input.read(buffer, 0, Math.min(buffer.length, remaining));
                if (read < 0) {
                    throw new RtspTransportException(
                        "RTSP response body ended before Content-Length bytes were read.");
                }

                body.write(buffer, 0, read);
                remaining -= read;
            }
        } catch (IOException ex) {
            throw new RtspTransportException("RTSP read failed: " + safeMessage(ex), ex);
        }

        return new String(body.toByteArray(), StandardCharsets.UTF_8);
    }

    private static int parseContentLength(String value) {
        if (value == null || value.trim().isEmpty()) {
            return 0;
        }

        try {
            int parsed = Integer.parseInt(value.trim());
            return Math.max(parsed, 0);
        } catch (NumberFormatException ex) {
            throw new RtspTransportException("RTSP response Content-Length is invalid: " + value + ".");
        }
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isEmpty() ? throwable.getClass().getSimpleName() : message;
    }
}
