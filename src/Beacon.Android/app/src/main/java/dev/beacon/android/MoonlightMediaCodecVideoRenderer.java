package dev.beacon.android;

import dev.beacon.streaming.moonlight.MoonlightNativeSessionPlan;
import dev.beacon.streaming.moonlight.MoonlightVideoRenderer;

import java.util.Arrays;

final class MoonlightMediaCodecVideoRenderer implements MoonlightVideoRenderer {
    private static final int H264Formats =
        MoonlightNativeSessionPlan.VIDEO_FORMAT_H264 |
            MoonlightNativeSessionPlan.VIDEO_FORMAT_H264_HIGH8_444;
    private static final int HevcFormats =
        MoonlightNativeSessionPlan.VIDEO_FORMAT_H265 |
            MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_MAIN10 |
            MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_REXT8_444 |
            MoonlightNativeSessionPlan.VIDEO_FORMAT_H265_REXT10_444;
    private static final int Av1Formats =
        MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_MAIN8 |
            MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_MAIN10 |
            MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_HIGH8_444 |
            MoonlightNativeSessionPlan.VIDEO_FORMAT_AV1_HIGH10_444;

    private final EncodedVideoCodecFactory codecFactory;
    private final EncodedVideoSurfaceProvider surfaceProvider;

    private EncodedVideoCodec codec;
    private MoonlightVideoSampleProvider samples;
    private boolean codecStarted;
    private String diagnostic = "";

    MoonlightMediaCodecVideoRenderer(
        EncodedVideoCodecFactory codecFactory,
        EncodedVideoSurfaceProvider surfaceProvider) {
        if (codecFactory == null) {
            throw new IllegalArgumentException("Encoded video codec factory is required.");
        }
        if (surfaceProvider == null) {
            throw new IllegalArgumentException("Encoded video surface provider is required.");
        }

        this.codecFactory = codecFactory;
        this.surfaceProvider = surfaceProvider;
    }

    @Override
    public int capabilities() {
        return 0;
    }

    @Override
    public synchronized int setup(int videoFormat, int width, int height, int fps) {
        cleanup();
        diagnostic = "";

        EncodedVideoCodec candidate = null;
        try {
            Object surface = surfaceProvider.currentSurface();
            if (surface == null) {
                throw new IllegalStateException("Encoded video surface is not ready.");
            }

            String codecName = codecName(videoFormat);
            EncodedVideoStreamPlan plan = EncodedVideoStreamPlan.nativeMoonlight(codecName, width, height, fps);
            MoonlightVideoSampleProvider candidateSamples = new MoonlightVideoSampleProvider();
            candidate = codecFactory.create(codecName);
            candidate.configure(plan, surface, candidateSamples);
            candidate.start();

            codec = candidate;
            samples = candidateSamples;
            codecStarted = true;
            return DR_OK;
        } catch (RuntimeException ex) {
            if (candidate != null) {
                candidate.release();
            }
            diagnostic = message(ex);
            return DR_NEED_IDR;
        }
    }

    @Override
    public void start() {
        // MediaCodec must be ready before native decode submissions begin.
    }

    @Override
    public synchronized void stop() {
        if (samples != null) {
            samples.finish();
        }
        if (codec != null && codecStarted) {
            codec.stop();
            codecStarted = false;
        }
    }

    @Override
    public synchronized void cleanup() {
        stop();
        if (codec != null) {
            codec.release();
            codec = null;
        }
        samples = null;
    }

    @Override
    public int submitDecodeUnit(
        byte[] data,
        int length,
        int bufferType,
        int frameType,
        int frameNumber,
        long presentationTimeUs) {
        MoonlightVideoSampleProvider activeSamples;
        synchronized (this) {
            activeSamples = samples;
            if (activeSamples == null || data == null || length <= 0 || length > data.length) {
                diagnostic = "Native video decode unit is invalid or the decoder is not ready.";
                return DR_NEED_IDR;
            }
        }

        try {
            activeSamples.submit(Arrays.copyOf(data, length), presentationTimeUs);
            return DR_OK;
        } catch (RuntimeException ex) {
            synchronized (this) {
                diagnostic = message(ex);
            }
            return DR_NEED_IDR;
        }
    }

    String diagnostic() {
        return diagnostic;
    }

    private static String codecName(int videoFormat) {
        if ((videoFormat & H264Formats) != 0) {
            return "h264";
        }
        if ((videoFormat & HevcFormats) != 0) {
            return "hevc";
        }
        if ((videoFormat & Av1Formats) != 0) {
            return "av1";
        }
        throw new IllegalArgumentException("Unsupported native Moonlight video format " + videoFormat + ".");
    }

    private static String message(RuntimeException ex) {
        return ex.getMessage() == null ? ex.getClass().getSimpleName() : ex.getMessage();
    }
}
