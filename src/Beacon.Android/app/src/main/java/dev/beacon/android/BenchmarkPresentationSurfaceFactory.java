package dev.beacon.android;

interface BenchmarkPresentationSurfaceFactory {
    BenchmarkPresentationSurface create(int width, int height, Observer observer);

    interface Observer {
        void onFramePresented(long presentationTimeUs, long presentedAtNs);
        void onFailure(Throwable failure);
    }
}
