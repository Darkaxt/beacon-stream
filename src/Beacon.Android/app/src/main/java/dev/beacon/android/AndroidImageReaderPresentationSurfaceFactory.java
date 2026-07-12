package dev.beacon.android;

import android.graphics.ImageFormat;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;

final class AndroidImageReaderPresentationSurfaceFactory
    implements BenchmarkPresentationSurfaceFactory {
    @Override
    public BenchmarkPresentationSurface create(int width, int height, Observer observer) {
        if (width <= 0 || height <= 0 || observer == null) {
            throw new IllegalArgumentException(
                "Presentation surface dimensions and observer are required.");
        }
        return new ImageReaderSurface(width, height, observer);
    }

    private static final class ImageReaderSurface implements BenchmarkPresentationSurface {
        private final ImageReader reader;
        private final HandlerThread callbackThread;
        private final Observer observer;
        private boolean closed;

        ImageReaderSurface(int width, int height, Observer observer) {
            this.observer = observer;
            callbackThread = new HandlerThread("beacon-benchmark-presentation");
            callbackThread.start();
            reader = ImageReader.newInstance(width, height, ImageFormat.PRIVATE, 8);
            reader.setOnImageAvailableListener(
                value -> drain(value, observer),
                new Handler(callbackThread.getLooper()));
        }

        @Override
        public Object surface() {
            return reader.getSurface();
        }

        @Override
        public synchronized void close() {
            if (closed) return;
            closed = true;
            reader.setOnImageAvailableListener(null, null);
            try {
                drainAvailable(reader);
            } finally {
                try {
                    reader.close();
                } finally {
                    callbackThread.quitSafely();
                }
            }
        }

        private synchronized void drain(ImageReader value, Observer observer) {
            if (closed) return;
            try {
                drainAvailable(value);
            } catch (RuntimeException failure) {
                observer.onFailure(failure);
            }
        }

        private void drainAvailable(ImageReader value) {
            while (true) {
                Image image = value.acquireNextImage();
                if (image == null) {
                    return;
                }
                try {
                    observer.onFramePresented(
                        image.getTimestamp() / 1_000L,
                        System.nanoTime());
                } finally {
                    image.close();
                }
            }
        }
    }
}
