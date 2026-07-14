package dev.beacon.android;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaFormat;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.view.Surface;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.Locale;
import java.util.OptionalLong;

public final class AndroidMediaCodecFactory implements EncodedVideoCodecFactory {
    @Override
    public EncodedVideoCodec create(String codec) {
        String mimeType = mimeType(codec);
        try {
            return new AndroidMediaCodec(MediaCodec.createDecoderByType(mimeType), mimeType);
        } catch (IOException | RuntimeException ex) {
            throw new IllegalStateException("Unable to create MediaCodec decoder for " + codec + ".", ex);
        }
    }

    private static String mimeType(String codec) {
        String normalized = codec == null ? "" : codec.trim().toLowerCase(Locale.ROOT);
        switch (normalized) {
            case "h264":
                return MediaFormat.MIMETYPE_VIDEO_AVC;
            case "hevc":
                return MediaFormat.MIMETYPE_VIDEO_HEVC;
            case "av1":
                return MediaFormat.MIMETYPE_VIDEO_AV1;
            default:
                throw new IllegalArgumentException("Unsupported encoded video codec '" + codec + "'.");
        }
    }

    private static final class AndroidMediaCodec implements EncodedVideoCodec {
        private final MediaCodec codec;
        private final String mimeType;
        private QueueingCallback callback;
        private HandlerThread callbackThread;

        AndroidMediaCodec(MediaCodec codec, String mimeType) {
            this.codec = codec;
            this.mimeType = mimeType;
        }

        @Override
        public void configure(EncodedVideoDecodeRequest request, Object surface, EncodedVideoSampleProvider sampleProvider) {
            configure(request, surface, sampleProvider, EncodedVideoCodecObserver.noOp());
        }

        @Override
        public void configure(
            EncodedVideoDecodeRequest request,
            Object surface,
            EncodedVideoSampleProvider sampleProvider,
            EncodedVideoCodecObserver observer) {
            if (!(surface instanceof Surface androidSurface)) {
                throw new IllegalStateException("Encoded video surface is not an Android Surface.");
            }

            if (sampleProvider == null) {
                throw new IllegalStateException("Encoded video sample provider is required.");
            }
            if (observer == null) {
                throw new IllegalStateException("Encoded video codec observer is required.");
            }

            callback = new QueueingCallback(sampleProvider, observer);
            callbackThread = new HandlerThread("beacon-mediacodec-callback");
            callbackThread.start();
            Handler callbackHandler = new Handler(callbackThread.getLooper());
            codec.setCallback(callback, callbackHandler);
            codec.setOnFrameRenderedListener(callback, callbackHandler);
            MediaFormat format = MediaFormat.createVideoFormat(mimeType, request.width(), request.height());
            format.setInteger(MediaFormat.KEY_FRAME_RATE, request.fps());
            if (MediaCodecLowLatencyPolicy.shouldEnable(
                Build.VERSION.SDK_INT,
                decoderSupportsLowLatency())) {
                format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1);
            }
            if (sampleProvider.maxSampleBytes() > 0) {
                format.setInteger(
                    MediaFormat.KEY_MAX_INPUT_SIZE,
                    sampleProvider.maxSampleBytes());
            }
            codec.configure(format, androidSurface, null, 0);
        }

        private boolean decoderSupportsLowLatency() {
            if (Build.VERSION.SDK_INT < Build.VERSION_CODES.R) return false;
            try {
                MediaCodecInfo.CodecCapabilities capabilities =
                    codec.getCodecInfo().getCapabilitiesForType(mimeType);
                return capabilities.isFeatureSupported(
                    MediaCodecInfo.CodecCapabilities.FEATURE_LowLatency);
            } catch (RuntimeException unavailable) {
                return false;
            }
        }

        @Override
        public void start() {
            codec.start();
        }

        @Override
        public void stop() {
            try {
                codec.stop();
            } finally {
                closeCallback();
            }
        }

        @Override
        public void release() {
            try {
                codec.release();
            } finally {
                closeCallback();
            }
        }

        private void closeCallback() {
            QueueingCallback owned = callback;
            callback = null;
            if (owned != null) owned.close();
            HandlerThread ownedThread = callbackThread;
            callbackThread = null;
            if (ownedThread != null) ownedThread.quitSafely();
        }

        private static final class QueueingCallback extends MediaCodec.Callback
            implements MediaCodec.OnFrameRenderedListener, AutoCloseable {
            private final EncodedVideoSampleProvider sampleProvider;
            private final EncodedVideoCodecObserver observer;
            private final SerialInputBufferFeeder feeder = SerialInputBufferFeeder.system();
            private final EncodedFramePresentationTracker presentationTracker =
                new EncodedFramePresentationTracker();
            private volatile boolean inputEnded;
            private volatile boolean closed;

            QueueingCallback(
                EncodedVideoSampleProvider sampleProvider,
                EncodedVideoCodecObserver observer) {
                this.sampleProvider = sampleProvider;
                this.observer = observer;
            }

            @Override
            public void onInputBufferAvailable(MediaCodec codec, int index) {
                if (closed) return;
                try {
                    feeder.submit(() -> queueAvailableInput(codec, index));
                } catch (RuntimeException failure) {
                    if (!closed) observer.onError(failure);
                }
            }

            private void queueAvailableInput(MediaCodec codec, int index) {
                if (inputEnded || closed) return;
                try {
                    EncodedVideoSample sample = sampleProvider.nextSample();
                    if (sample.endOfStream()) {
                        inputEnded = true;
                        codec.queueInputBuffer(
                            index,
                            0,
                            0,
                            sample.presentationTimeUs(),
                            MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                        return;
                    }

                    ByteBuffer inputBuffer = codec.getInputBuffer(index);
                    ByteBuffer data = sample.dataBuffer();
                    int dataLength = data.remaining();
                    if (inputBuffer == null || dataLength > inputBuffer.capacity()) {
                        throw new IllegalStateException(
                            "MediaCodec input buffer cannot hold the benchmark access unit.");
                    }

                    inputBuffer.clear();
                    inputBuffer.put(data);
                    presentationTracker.track(
                        sample.sequence(),
                        sample.presentationTimeUs());
                    try {
                        codec.queueInputBuffer(
                            index,
                            0,
                            dataLength,
                            sample.presentationTimeUs(),
                            0);
                    } catch (RuntimeException failure) {
                        presentationTracker.discard(
                            sample.sequence(),
                            sample.presentationTimeUs());
                        throw failure;
                    }
                    observer.onInputQueued(
                        sample.sequence(),
                        sample.presentationTimeUs(),
                        System.nanoTime());
                } catch (RuntimeException failure) {
                    if (!closed) observer.onError(failure);
                    inputEnded = true;
                }
            }

            @Override
            public void onOutputBufferAvailable(MediaCodec codec, int index, MediaCodec.BufferInfo info) {
                if (closed) return;
                OptionalLong frameSequence = OptionalLong.empty();
                try {
                    boolean endOfStream =
                        (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
                    boolean rendered = info.size > 0 &&
                        (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0;
                    frameSequence = rendered
                        ? presentationTracker.markOutputReleased(info.presentationTimeUs)
                        : OptionalLong.empty();
                    codec.releaseOutputBuffer(index, rendered);
                    if (frameSequence.isPresent()) {
                        observer.onOutputReleased(
                            frameSequence.getAsLong(),
                            info.presentationTimeUs,
                            System.nanoTime(),
                            true);
                    } else {
                        observer.onError(new IllegalStateException(
                            "MediaCodec output has no queued Beacon frame identity."));
                    }
                    if (endOfStream) observer.onEndOfStream();
                } catch (RuntimeException failure) {
                    if (frameSequence.isPresent()) {
                        presentationTracker.discard(
                            frameSequence.getAsLong(),
                            info.presentationTimeUs);
                    }
                    if (!closed) observer.onError(failure);
                }
            }

            @Override
            public void onFrameRendered(
                MediaCodec codec,
                long presentationTimeUs,
                long nanoTime) {
                if (closed) return;
                OptionalLong frameSequence = presentationTracker.takeRendered(
                    presentationTimeUs);
                if (frameSequence.isPresent()) {
                    observer.onFrameRendered(
                        frameSequence.getAsLong(),
                        presentationTimeUs,
                        nanoTime);
                } else {
                    observer.onError(new IllegalStateException(
                        "Rendered MediaCodec frame has no queued Beacon frame identity."));
                }
            }

            @Override
            public void onError(MediaCodec codec, MediaCodec.CodecException exception) {
                if (closed) return;
                observer.onError(new EncodedVideoCodecFailure(
                    exception.getErrorCode(),
                    exception));
            }

            @Override
            public void onOutputFormatChanged(MediaCodec codec, MediaFormat format) {
            }

            @Override
            public void close() {
                closed = true;
                inputEnded = true;
                feeder.close();
                presentationTracker.clear();
            }
        }
    }
}
