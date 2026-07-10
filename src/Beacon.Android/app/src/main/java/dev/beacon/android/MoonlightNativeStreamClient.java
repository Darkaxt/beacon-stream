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
    private final Object sessionGate = new Object();
    private ActiveSession activeSession;

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

        ActiveSession session = new ActiveSession(renderer);
        synchronized (sessionGate) {
            activeSession = session;
        }

        MoonlightNativeStartResult nativeResult;
        try {
            nativeResult = connection.start(connectionDescriptor.nativeSession(), renderer, ConnectionListener);
        } catch (RuntimeException ex) {
            releaseFailedStart(session);
            return NativeStreamStartResult.unsupported("Moonlight native connection failed: " + safeMessage(ex));
        }

        if (!nativeResult.success()) {
            releaseFailedStart(session);
            return NativeStreamStartResult.unsupported(nativeResult.diagnostic());
        }

        boolean sessionStillActive;
        synchronized (sessionGate) {
            sessionStillActive = activeSession == session;
        }
        if (!sessionStillActive) {
            session.cleanup();
            return NativeStreamStartResult.unsupported(
                "Moonlight native connection was stopped while starting.");
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
        ActiveSession session;
        synchronized (sessionGate) {
            session = activeSession;
            activeSession = null;
        }
        if (session == null) {
            return;
        }

        try {
            connection.stop();
        } finally {
            session.cleanup();
        }
    }

    private void releaseFailedStart(ActiveSession session) {
        synchronized (sessionGate) {
            if (activeSession == session) {
                activeSession = null;
            }
        }
        session.cleanup();
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isBlank() ? throwable.getClass().getSimpleName() : message;
    }

    private static final class ActiveSession {
        private final MoonlightVideoRenderer renderer;
        private boolean cleaned;

        private ActiveSession(MoonlightVideoRenderer renderer) {
            this.renderer = renderer;
        }

        private synchronized void cleanup() {
            if (cleaned) {
                return;
            }
            cleaned = true;
            renderer.cleanup();
        }
    }
}
