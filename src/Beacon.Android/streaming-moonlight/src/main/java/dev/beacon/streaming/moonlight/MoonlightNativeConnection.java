package dev.beacon.streaming.moonlight;

public final class MoonlightNativeConnection {
    private final Object gate = new Object();
    private final Bindings bindings;
    private State state = State.IDLE;

    public MoonlightNativeConnection() {
        this(new JniBindings());
    }

    public static String bindingIdentity() {
        if (!MoonlightNativeCore.isAvailable()) {
            throw new IllegalStateException(MoonlightNativeCore.diagnostic());
        }
        return nativeConnectionIdentity();
    }

    MoonlightNativeConnection(Bindings bindings) {
        if (bindings == null) {
            throw new IllegalArgumentException("Moonlight native bindings are required.");
        }
        this.bindings = bindings;
    }

    public MoonlightNativeStartResult start(
        MoonlightNativeSessionPlan plan,
        MoonlightVideoRenderer renderer,
        MoonlightConnectionListener listener) {
        if (plan == null) {
            return MoonlightNativeStartResult.failed(-1, "Moonlight native session plan is required.");
        }
        if (renderer == null) {
            return MoonlightNativeStartResult.failed(-1, "Moonlight video renderer is required.");
        }
        if (listener == null) {
            return MoonlightNativeStartResult.failed(-1, "Moonlight connection listener is required.");
        }
        if (!bindings.available()) {
            return MoonlightNativeStartResult.failed(-1, bindings.unavailableDiagnostic());
        }

        synchronized (gate) {
            if (state != State.IDLE) {
                return MoonlightNativeStartResult.failed(-1, "Moonlight native connection is already active.");
            }
            state = State.STARTING;
        }

        int result;
        try {
            result = bindings.start(plan, renderer, listener);
        } catch (RuntimeException ex) {
            synchronized (gate) {
                state = State.IDLE;
            }
            return MoonlightNativeStartResult.failed(-1, "Moonlight native connection failed: " + safeMessage(ex));
        }

        synchronized (gate) {
            if (state == State.INTERRUPTING) {
                state = State.IDLE;
                return MoonlightNativeStartResult.failed(
                    result,
                    "Moonlight native connection start was interrupted. Error code " + result + '.');
            }
            if (result != 0) {
                state = State.IDLE;
                return MoonlightNativeStartResult.failed(
                    result,
                    "Moonlight native connection failed with error code " + result + '.');
            }

            state = State.RUNNING;
            return MoonlightNativeStartResult.started();
        }
    }

    public void stop() {
        Action action;
        synchronized (gate) {
            action = switch (state) {
                case STARTING -> {
                    state = State.INTERRUPTING;
                    yield Action.INTERRUPT;
                }
                case RUNNING -> {
                    state = State.STOPPING;
                    yield Action.STOP;
                }
                case IDLE, INTERRUPTING, STOPPING -> Action.NONE;
            };
        }

        if (action == Action.INTERRUPT) {
            bindings.interrupt();
            return;
        }
        if (action == Action.STOP) {
            try {
                bindings.stop();
            } finally {
                synchronized (gate) {
                    state = State.IDLE;
                }
            }
        }
    }

    public boolean active() {
        synchronized (gate) {
            return state != State.IDLE;
        }
    }

    interface Bindings {
        boolean available();

        String unavailableDiagnostic();

        int start(
            MoonlightNativeSessionPlan plan,
            MoonlightVideoRenderer renderer,
            MoonlightConnectionListener listener);

        void stop();

        void interrupt();
    }

    private static final class JniBindings implements Bindings {
        @Override
        public boolean available() {
            return MoonlightNativeCore.isAvailable();
        }

        @Override
        public String unavailableDiagnostic() {
            return MoonlightNativeCore.diagnostic();
        }

        @Override
        public int start(
            MoonlightNativeSessionPlan plan,
            MoonlightVideoRenderer renderer,
            MoonlightConnectionListener listener) {
            return nativeStart(
                plan.address(),
                plan.serverAppVersion(),
                plan.serverGfeVersion(),
                plan.rtspSessionUrl(),
                plan.serverCodecModeSupport(),
                plan.width(),
                plan.height(),
                plan.fps(),
                plan.bitrateKbps(),
                plan.packetSize(),
                plan.streamingRemotely(),
                plan.audioConfiguration(),
                plan.supportedVideoFormats(),
                plan.clientRefreshRateX100(),
                plan.colorSpace(),
                plan.colorRange(),
                plan.encryptionFlags(),
                plan.remoteInputAesKey(),
                plan.remoteInputAesIv(),
                renderer.capabilities(),
                renderer,
                listener);
        }

        @Override
        public void stop() {
            nativeStop();
        }

        @Override
        public void interrupt() {
            nativeInterrupt();
        }
    }

    private static String safeMessage(Throwable throwable) {
        String message = throwable.getMessage();
        return message == null || message.isBlank() ? throwable.getClass().getSimpleName() : message;
    }

    private static native int nativeStart(
        String address,
        String serverAppVersion,
        String serverGfeVersion,
        String rtspSessionUrl,
        int serverCodecModeSupport,
        int width,
        int height,
        int fps,
        int bitrateKbps,
        int packetSize,
        int streamingRemotely,
        int audioConfiguration,
        int supportedVideoFormats,
        int clientRefreshRateX100,
        int colorSpace,
        int colorRange,
        int encryptionFlags,
        byte[] remoteInputAesKey,
        byte[] remoteInputAesIv,
        int videoCapabilities,
        MoonlightVideoRenderer renderer,
        MoonlightConnectionListener listener);

    private static native String nativeConnectionIdentity();

    private static native void nativeStop();

    private static native void nativeInterrupt();

    private enum State {
        IDLE,
        STARTING,
        RUNNING,
        INTERRUPTING,
        STOPPING
    }

    private enum Action {
        NONE,
        INTERRUPT,
        STOP
    }
}
