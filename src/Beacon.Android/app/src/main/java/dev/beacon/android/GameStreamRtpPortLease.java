package dev.beacon.android;

import java.io.IOException;

public final class GameStreamRtpPortLease {
    private final int audioClientPort;
    private final int videoClientPort;
    private final int controlClientPort;
    private RtpDatagramSocket audioSocket;
    private RtpDatagramSocket videoSocket;
    private RtpDatagramSocket controlSocket;
    private boolean videoSocketTransferred;
    private boolean closed;

    private GameStreamRtpPortLease(
        RtpDatagramSocket audioSocket,
        RtpDatagramSocket videoSocket,
        RtpDatagramSocket controlSocket,
        int audioClientPort,
        int videoClientPort,
        int controlClientPort) {
        this.audioSocket = audioSocket;
        this.videoSocket = videoSocket;
        this.controlSocket = controlSocket;
        this.audioClientPort = audioClientPort;
        this.videoClientPort = videoClientPort;
        this.controlClientPort = controlClientPort;
    }

    public static GameStreamRtpPortLease open(RtpDatagramSocketFactory factory) {
        if (factory == null) {
            throw new IllegalArgumentException("RTP datagram socket factory is required.");
        }

        RtpDatagramSocket audio = null;
        RtpDatagramSocket video = null;
        RtpDatagramSocket control = null;
        try {
            audio = bindDynamic(factory);
            video = bindDynamic(factory);
            control = bindDynamic(factory);
            return new GameStreamRtpPortLease(
                audio,
                video,
                control,
                audio.localPort(),
                video.localPort(),
                control.localPort());
        } catch (IOException ex) {
            closeQuietly(audio);
            closeQuietly(video);
            closeQuietly(control);
            throw new IllegalStateException("RTP UDP port lease failed: " + safeMessage(ex), ex);
        } catch (RuntimeException ex) {
            closeQuietly(audio);
            closeQuietly(video);
            closeQuietly(control);
            throw ex;
        }
    }

    public static GameStreamRtpPortLease staticPorts(
        int audioClientPort,
        int videoClientPort,
        int controlClientPort) {
        return new GameStreamRtpPortLease(
            null,
            null,
            null,
            audioClientPort,
            videoClientPort,
            controlClientPort);
    }

    public int audioClientPort() {
        return audioClientPort;
    }

    public int videoClientPort() {
        return videoClientPort;
    }

    public int controlClientPort() {
        return controlClientPort;
    }

    public RtpDatagramSocket takeVideoSocket() {
        if (videoSocketTransferred) {
            return null;
        }

        videoSocketTransferred = true;
        RtpDatagramSocket socket = videoSocket;
        videoSocket = null;
        return socket;
    }

    public void close() {
        if (closed) {
            return;
        }

        closed = true;
        closeQuietly(audioSocket);
        if (!videoSocketTransferred) {
            closeQuietly(videoSocket);
        }
        closeQuietly(controlSocket);
        audioSocket = null;
        videoSocket = null;
        controlSocket = null;
    }

    private static RtpDatagramSocket bindDynamic(RtpDatagramSocketFactory factory) throws IOException {
        RtpDatagramSocket socket = factory.bind(0);
        if (socket == null) {
            throw new IOException("socket factory returned no socket");
        }

        return socket;
    }

    private static void closeQuietly(RtpDatagramSocket socket) {
        if (socket == null) {
            return;
        }

        try {
            socket.close();
        } catch (RuntimeException ignored) {
        }
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isEmpty() ? throwable.getClass().getSimpleName() : message;
    }
}
