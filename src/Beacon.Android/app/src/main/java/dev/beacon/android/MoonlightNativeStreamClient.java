package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightConnectionListener;
import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;
import dev.beacon.streaming.moonlight.MoonlightNativeStartResult;
import dev.beacon.streaming.moonlight.MoonlightVideoRenderer;

final class MoonlightNativeStreamClient implements NativeStreamProtocolClient {
    private static final MoonlightConnectionListener ConnectionListener = new MoonlightConnectionListener() {
    };

    private final MoonlightStreamConnection connection;
    private final MoonlightVideoRendererFactory rendererFactory;
    private MoonlightVideoRenderer activeRenderer;
    private boolean connectionOwned;

    MoonlightNativeStreamClient(
        MoonlightStreamConnection connection,
        MoonlightVideoRendererFactory rendererFactory) {
        if (connection == null) {
            throw new IllegalArgumentException("Moonlight stream connection is required.");
        }
        if (rendererFactory == null) {
            throw new IllegalArgumentException("Moonlight video renderer factory is required.");
        }
        this.connection = connection;
        this.rendererFactory = rendererFactory;
    }

    @Override
    public boolean supports(StreamConnectionDescriptor connectionDescriptor) {
        return connectionDescriptor != null && connectionDescriptor.nativeSessionProvided();
    }

    @Override
    public NativeStreamStartResult start(StreamConnectionDescriptor connectionDescriptor) {
        stop();
        if (connectionDescriptor == null || !connectionDescriptor.nativeSessionValid()) {
            return NativeStreamStartResult.unsupported(
                connectionDescriptor == null ? "" : connectionDescriptor.nativeSessionDiagnostic());
        }

        MoonlightVideoRenderer renderer;
        try {
            renderer = rendererFactory.create();
        } catch (RuntimeException ex) {
            return NativeStreamStartResult.unsupported("Moonlight video renderer failed: " + safeMessage(ex));
        }
        if (renderer == null) {
            return NativeStreamStartResult.unsupported("Moonlight video renderer factory returned no renderer.");
        }

        activeRenderer = renderer;
        connectionOwned = true;
        MoonlightNativeStartResult nativeResult;
        try {
            nativeResult = connection.start(connectionDescriptor.nativeSession(), renderer, ConnectionListener);
        } catch (RuntimeException ex) {
            releaseFailedStart(renderer);
            return NativeStreamStartResult.unsupported("Moonlight native connection failed: " + safeMessage(ex));
        }

        if (!nativeResult.success()) {
            releaseFailedStart(renderer);
            return NativeStreamStartResult.unsupported(nativeResult.diagnostic());
        }

        MoonlightNativeSessionPlan plan = connectionDescriptor.nativeSession();
        return NativeStreamStartResult.started(
            "Native Moonlight stream started. rtsp=" + plan.rtspSessionUrl(),
            NativeStreamPresentation.encodedVideo(
                plan.rtspSessionUrl(),
                "moonlight-native",
                plan.width(),
                plan.height(),
                plan.fps()));
    }

    @Override
    public void stop() {
        MoonlightVideoRenderer renderer = activeRenderer;
        boolean stopConnection = connectionOwned;
        activeRenderer = null;
        connectionOwned = false;

        try {
            if (stopConnection) {
                connection.stop();
            }
        } finally {
            if (renderer != null) {
                renderer.cleanup();
            }
        }
    }

    private void releaseFailedStart(MoonlightVideoRenderer renderer) {
        if (activeRenderer == renderer) {
            activeRenderer = null;
        }
        connectionOwned = false;
        renderer.cleanup();
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isBlank() ? throwable.getClass().getSimpleName() : message;
    }
}
