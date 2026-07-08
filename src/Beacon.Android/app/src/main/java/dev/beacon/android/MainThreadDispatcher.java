package dev.beacon.android;

public interface MainThreadDispatcher {
    void dispatch(Runnable action);
}
