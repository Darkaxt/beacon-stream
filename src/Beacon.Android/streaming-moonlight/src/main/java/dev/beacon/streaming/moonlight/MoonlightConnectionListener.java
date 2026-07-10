package dev.beacon.streaming.moonlight;

public interface MoonlightConnectionListener {
    default void stageStarting(int stage) {
    }

    default void stageComplete(int stage) {
    }

    default void stageFailed(int stage, int errorCode) {
    }

    default void connectionStarted() {
    }

    default void connectionTerminated(int errorCode) {
    }

    default void connectionStatusUpdate(int connectionStatus) {
    }

    default void setHdrMode(boolean enabled) {
    }
}
