package dev.beacon.android;

import android.media.MediaCodec;
import android.media.MediaFormat;
import android.view.Surface;

import java.io.IOException;
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
        public void configure(EncodedVideoStreamPlan plan, Object surface) {
            if (!(surface instanceof Surface androidSurface)) {
                throw new IllegalStateException("Encoded video surface is not an Android Surface.");
            }

            MediaFormat format = MediaFormat.createVideoFormat(mimeType, plan.width(), plan.height());
            format.setInteger(MediaFormat.KEY_FRAME_RATE, plan.fps());
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
    }
}
