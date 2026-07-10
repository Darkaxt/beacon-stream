package dev.beacon.streaming.moonlight;

public final class MoonlightNativeCore {
    private static final String LIBRARY_NAME = "beacon-moonlight-core";
    private static final String CORE_IDENTITY = "moonlight-common-c";
    private static final LoadState LOAD_STATE = load();

    private MoonlightNativeCore() {
    }

    public static boolean isAvailable() {
        return LOAD_STATE.available;
    }

    public static String diagnostic() {
        return LOAD_STATE.diagnostic;
    }

    public static String identity() {
        requireAvailable();
        return LOAD_STATE.identity;
    }

    public static String stageName(int stage) {
        requireAvailable();
        return nativeStageName(stage);
    }

    private static LoadState load() {
        try {
            System.loadLibrary(LIBRARY_NAME);
            if (!CORE_IDENTITY.equals(nativeIdentity())) {
                return LoadState.unavailable("Moonlight native core returned an unexpected identity.");
            }

            return LoadState.available(CORE_IDENTITY);
        } catch (LinkageError error) {
            return LoadState.unavailable("Moonlight native core unavailable: " + error.getMessage());
        }
    }

    private static void requireAvailable() {
        if (!LOAD_STATE.available) {
            throw new IllegalStateException(LOAD_STATE.diagnostic);
        }
    }

    private static native String nativeIdentity();

    private static native String nativeStageName(int stage);

    private static final class LoadState {
        final boolean available;
        final String identity;
        final String diagnostic;

        private LoadState(boolean available, String identity, String diagnostic) {
            this.available = available;
            this.identity = identity;
            this.diagnostic = diagnostic;
        }

        static LoadState available(String identity) {
            return new LoadState(true, identity, "Moonlight native core loaded.");
        }

        static LoadState unavailable(String diagnostic) {
            return new LoadState(false, "", diagnostic);
        }
    }
}
