package dev.beacon.android;

import android.media.MediaCodec;
import android.media.MediaFormat;
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

        AndroidMediaCodec(MediaCodec codec, String mimeType) {
            this.codec = codec;
            this.mimeType = mimeType;
        }

        @Override
        public void configure(EncodedVideoDecodeRequest request, Object surface, EncodedVideoSampleProvider sampleProvider) {
            if (!(surface instanceof Surface androidSurface)) {
                throw new IllegalStateException("Encoded video surface is not an Android Surface.");
            }

            if (sampleProvider == null) {
                throw new IllegalStateException("Encoded video sample provider is required.");
            }

            codec.setCallback(new QueueingCallback(sampleProvider));
            MediaFormat format = MediaFormat.createVideoFormat(mimeType, request.width(), request.height());
            format.setInteger(MediaFormat.KEY_FRAME_RATE, request.fps());
            codec.configure(format, androidSurface, null, 0);
        }

        @Override
        public void start() {
            codec.start();
        }

        @Override
        public void stop() {
            codec.stop();
        }

        @Override
        public void release() {
            codec.release();
        }

        private static final class QueueingCallback extends MediaCodec.Callback {
            private final EncodedVideoSampleProvider sampleProvider;

            QueueingCallback(EncodedVideoSampleProvider sampleProvider) {
                this.sampleProvider = sampleProvider;
            }

            @Override
            public void onInputBufferAvailable(MediaCodec codec, int index) {
                EncodedVideoSample sample = sampleProvider.nextSample();
                if (sample.endOfStream()) {
                    codec.queueInputBuffer(index, 0, 0, sample.presentationTimeUs(), MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                    return;
                }

                ByteBuffer inputBuffer = codec.getInputBuffer(index);
                byte[] data = sample.data();
                if (inputBuffer == null || data.length > inputBuffer.capacity()) {
                    codec.queueInputBuffer(index, 0, 0, sample.presentationTimeUs(), MediaCodec.BUFFER_FLAG_END_OF_STREAM);
                    return;
                }

                inputBuffer.clear();
                inputBuffer.put(data);
                codec.queueInputBuffer(index, 0, data.length, sample.presentationTimeUs(), 0);
            }

            @Override
            public void onOutputBufferAvailable(MediaCodec codec, int index, MediaCodec.BufferInfo info) {
                codec.releaseOutputBuffer(index, info.size > 0);
            }

            @Override
            public void onError(MediaCodec codec, MediaCodec.CodecException exception) {
            }

            @Override
            public void onOutputFormatChanged(MediaCodec codec, MediaFormat format) {
            }
        }
    }
}
