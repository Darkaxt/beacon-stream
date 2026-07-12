package dev.beacon.android;

import android.media.MediaCodec;
import android.media.MediaFormat;
import android.os.Build;
import android.view.Surface;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.Locale;

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
            codec.setCallback(callback);
            MediaFormat format = MediaFormat.createVideoFormat(mimeType, request.width(), request.height());
            format.setInteger(MediaFormat.KEY_FRAME_RATE, request.fps());
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.R) {
                format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1);
            }
            if (sampleProvider.maxSampleBytes() > 0) {
                format.setInteger(
                    MediaFormat.KEY_MAX_INPUT_SIZE,
                    sampleProvider.maxSampleBytes());
            }
            codec.configure(format, androidSurface, null, 0);
        }

        @Override
        public void start() {
            codec.start();
        }

        @Override
        public void stop() {
            closeCallback();
            codec.stop();
        }

        @Override
        public void release() {
            closeCallback();
            codec.release();
        }

        private void closeCallback() {
            QueueingCallback owned = callback;
            callback = null;
            if (owned != null) owned.close();
        }

        private static final class QueueingCallback extends MediaCodec.Callback
            implements AutoCloseable {
            private final EncodedVideoSampleProvider sampleProvider;
            private final EncodedVideoCodecObserver observer;
            private final SerialInputBufferFeeder feeder = SerialInputBufferFeeder.system();
            private boolean inputEnded;

            QueueingCallback(
                EncodedVideoSampleProvider sampleProvider,
                EncodedVideoCodecObserver observer) {
                this.sampleProvider = sampleProvider;
                this.observer = observer;
            }

            @Override
            public void onInputBufferAvailable(MediaCodec codec, int index) {
                try {
                    feeder.submit(() -> queueAvailableInput(codec, index));
                } catch (RuntimeException failure) {
                    observer.onError(failure);
                }
            }

            private void queueAvailableInput(MediaCodec codec, int index) {
                if (inputEnded) return;
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
                    byte[] data = sample.data();
                    if (inputBuffer == null || data.length > inputBuffer.capacity()) {
                        throw new IllegalStateException(
                            "MediaCodec input buffer cannot hold the benchmark access unit.");
                    }

                    inputBuffer.clear();
                    inputBuffer.put(data);
                    codec.queueInputBuffer(index, 0, data.length, sample.presentationTimeUs(), 0);
                    observer.onInputQueued(sample.presentationTimeUs(), System.nanoTime());
                } catch (RuntimeException failure) {
                    observer.onError(failure);
                    inputEnded = true;
                }
            }

            @Override
            public void onOutputBufferAvailable(MediaCodec codec, int index, MediaCodec.BufferInfo info) {
                boolean endOfStream =
                    (info.flags & MediaCodec.BUFFER_FLAG_END_OF_STREAM) != 0;
                boolean rendered = info.size > 0 &&
                    (info.flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0;
                codec.releaseOutputBuffer(index, rendered);
                if (rendered) {
                    observer.onOutputReleased(
                        info.presentationTimeUs,
                        System.nanoTime(),
                        true);
                }
                if (endOfStream) {
                    observer.onEndOfStream();
                }
            }

            @Override
            public void onError(MediaCodec codec, MediaCodec.CodecException exception) {
                observer.onError(exception);
            }

            @Override
            public void onOutputFormatChanged(MediaCodec codec, MediaFormat format) {
            }

            @Override
            public void close() {
                feeder.close();
            }
        }
    }
}
