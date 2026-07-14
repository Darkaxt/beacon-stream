package dev.beacon.android;

public interface EncodedVideoSurfaceProvider {
    Object currentSurface();

    default void setObserver(Observer observer) { }

    interface Observer {
        void onSurfaceAvailable();
        void onSurfaceDestroyed();

        static Observer noOp() {
            return new Observer() {
                @Override public void onSurfaceAvailable() { }
                @Override public void onSurfaceDestroyed() { }
            };
        }
    }
}
