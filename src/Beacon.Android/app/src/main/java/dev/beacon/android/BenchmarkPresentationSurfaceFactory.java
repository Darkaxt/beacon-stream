package dev.beacon.android;

interface BenchmarkPresentationSurfaceFactory {
    BenchmarkPresentationSurface create(int width, int height, Observer observer);

    default BenchmarkPresentationSurface create(
        EncodedVideoDecodeRequest request,
        Observer observer) {
        return create(request.width(), request.height(), observer);
    }

    interface Observer {
        void onFramePresented(long presentationTimeUs, long presentedAtNs);
        void onFailure(Throwable failure);
    }
}
