package dev.beacon.android;

interface BenchmarkPresentationSurface extends AutoCloseable {
    Object surface();
    default boolean physicalDisplaySurface() { return false; }
    default boolean hdr10PresentationVerified() { return false; }
    default void onFrameRendered() { }
    @Override void close();
}
