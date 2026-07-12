package dev.beacon.android;

interface BenchmarkPresentationSurface extends AutoCloseable {
    Object surface();
    @Override void close();
}
