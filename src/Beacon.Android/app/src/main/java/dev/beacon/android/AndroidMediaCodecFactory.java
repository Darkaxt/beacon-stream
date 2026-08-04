package dev.beacon.android;

import android.media.MediaCodec;
import android.media.MediaCodecInfo;
import android.media.MediaCodecList;
import android.media.MediaFormat;
import android.os.Build;
import android.os.Handler;
import android.os.HandlerThread;
import android.view.Surface;

import java.io.IOException;
import java.nio.ByteBuffer;
import java.util.Arrays;
import java.util.Locale;
import java.util.OptionalLong;

public final class AndroidMediaCodecFactory implements EncodedVideoCodecFactory {
    @Override
    public EncodedVideoCodec create(String codec) {
        return new AndroidMediaCodec(mimeType(codec));
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

    static boolean outputRequiresFrameIdentity(int outputBytes, int flags) {
        return outputBytes > 0 &&
            (flags & MediaCodec.BUFFER_FLAG_CODEC_CONFIG) == 0;
    }

    static MediaFormat buildMediaFormat(
        EncodedVideoDecodeRequest request,
        int maxInputBytes,
        boolean lowLatency) {
        MediaFormat format = MediaFormat.createVideoFormat(
            mimeType(request.codec()), request.width(), request.height());
        format.setInteger(MediaFormat.KEY_FRAME_RATE, request.fps());
        if (request.isHevcMain10Hdr10()) {
            format.setInteger(
                MediaFormat.KEY_PROFILE,
                MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10);
            format.setInteger(
                MediaFormat.KEY_COLOR_STANDARD,
                MediaFormat.COLOR_STANDARD_BT2020);
            format.setInteger(
                MediaFormat.KEY_COLOR_TRANSFER,
                MediaFormat.COLOR_TRANSFER_ST2084);
            format.setInteger(
                MediaFormat.KEY_COLOR_RANGE,
                MediaFormat.COLOR_RANGE_LIMITED);
            format.setByteBuffer(
                MediaFormat.KEY_HDR_STATIC_INFO,
                ByteBuffer.wrap(request.hdrStaticInfo()).asReadOnlyBuffer());
        }
        if (lowLatency) format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1);
        if (maxInputBytes > 0) {
            format.setInteger(MediaFormat.KEY_MAX_INPUT_SIZE, maxInputBytes);
        }
        return format;
    }

    static boolean outputFormatMatches(
        EncodedVideoDecodeRequest request,
        EncodedVideoOutputFormat output) {
        return outputFormatMatches(request, output, true);
    }

    static boolean outputFormatMatches(
        EncodedVideoDecodeRequest request,
        EncodedVideoOutputFormat output,
        boolean decoderHdr10ProfileSupported) {
        if (!request.isHevcMain10Hdr10()) return true;
        boolean profileMatches = output.profile() == -1 ||
            output.profile() == MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10;
        byte[] outputStaticInfo = output.hdrStaticInfo();
        boolean staticInfoMatches = outputStaticInfo.length == 0
            ? request.hdrStaticInfoInBitstream()
            : Arrays.equals(request.hdrStaticInfo(), outputStaticInfo);
        return MediaFormat.MIMETYPE_VIDEO_HEVC.equalsIgnoreCase(output.mimeType()) &&
            decoderHdr10ProfileSupported &&
            profileMatches &&
            output.colorStandard() == MediaFormat.COLOR_STANDARD_BT2020 &&
            output.colorTransfer() == MediaFormat.COLOR_TRANSFER_ST2084 &&
            output.colorRange() == MediaFormat.COLOR_RANGE_LIMITED &&
            staticInfoMatches;
    }

    private static EncodedVideoOutputFormat outputFormat(MediaFormat format) {
        return new EncodedVideoOutputFormat(
            format.getString(MediaFormat.KEY_MIME),
            integer(format, MediaFormat.KEY_PROFILE),
            integer(format, MediaFormat.KEY_COLOR_STANDARD),
            integer(format, MediaFormat.KEY_COLOR_TRANSFER),
            integer(format, MediaFormat.KEY_COLOR_RANGE),
            bytes(format.getByteBuffer(MediaFormat.KEY_HDR_STATIC_INFO)));
    }

    private static int integer(MediaFormat format, String key) {
        return format.containsKey(key) ? format.getInteger(key) : -1;
    }

    private static byte[] bytes(ByteBuffer value) {
        if (value == null) return new byte[0];
        ByteBuffer copy = value.asReadOnlyBuffer();
        byte[] result = new byte[copy.remaining()];
        copy.get(result);
        return result;
    }

    private static final class AndroidMediaCodec implements EncodedVideoCodec {
        private final String mimeType;
        private MediaCodec codec;
        private QueueingCallback callback;
        private HandlerThread callbackThread;

        AndroidMediaCodec(String mimeType) {
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

            MediaFormat format = buildMediaFormat(
                request, sampleProvider.maxSampleBytes(), false);
            String decoderName = new MediaCodecList(MediaCodecList.REGULAR_CODECS)
                .findDecoderForFormat(format);
            if (decoderName == null || decoderName.isBlank()) {
                throw new IllegalStateException(
                    "No MediaCodec decoder supports the exact selected video format.");
            }
            try {
                codec = MediaCodec.createByCodecName(decoderName);
                boolean decoderHdr10ProfileSupported =
                    !request.isHevcMain10Hdr10() || decoderSupportsHdr10Profile();
                if (!decoderHdr10ProfileSupported) {
                    throw new IllegalStateException(
                        "Selected MediaCodec decoder does not advertise an HEVC HDR10 profile.");
                }
                if (MediaCodecLowLatencyPolicy.shouldEnable(
                    Build.VERSION.SDK_INT,
                    decoderSupportsLowLatency())) {
                    format.setInteger(MediaFormat.KEY_LOW_LATENCY, 1);
                }
                callback = new QueueingCallback(
                    request,
                    sampleProvider,
                    observer,
                    decoderHdr10ProfileSupported);
                callbackThread = new HandlerThread("beacon-mediacodec-callback");
                callbackThread.start();
                Handler callbackHandler = new Handler(callbackThread.getLooper());
                codec.setCallback(callback, callbackHandler);
                codec.setOnFrameRenderedListener(callback, callbackHandler);
                codec.configure(format, androidSurface, null, 0);
            } catch (IOException | RuntimeException failure) {
                closeCallback();
                MediaCodec owned = codec;
                codec = null;
                if (owned != null) owned.release();
                throw new IllegalStateException(
                    "Unable to configure exact MediaCodec decoder " + decoderName + ".",
                    failure);
            }
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

        private boolean decoderSupportsHdr10Profile() {
            try {
                MediaCodecInfo.CodecCapabilities capabilities =
                    codec.getCodecInfo().getCapabilitiesForType(mimeType);
                for (MediaCodecInfo.CodecProfileLevel level : capabilities.profileLevels) {
                    if (level.profile ==
                            MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10 ||
                        level.profile ==
                            MediaCodecInfo.CodecProfileLevel.HEVCProfileMain10HDR10Plus) {
                        return true;
                    }
                }
                return false;
            } catch (RuntimeException unavailable) {
                return false;
            }
        }

        @Override
        public void start() {
            if (codec == null) throw new IllegalStateException("MediaCodec is not configured.");
            codec.start();
        }

        @Override
        public void stop() {
            MediaCodec owned = codec;
            if (owned == null) return;
            try {
                owned.stop();
            } finally {
                closeCallback();
            }
        }

        @Override
        public void release() {
            MediaCodec owned = codec;
            codec = null;
            if (owned == null) {
                closeCallback();
                return;
            }
            try {
                owned.release();
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
            private final EncodedVideoDecodeRequest request;
            private final boolean decoderHdr10ProfileSupported;
            private final SerialInputBufferFeeder feeder = SerialInputBufferFeeder.system();
            private final EncodedFramePresentationTracker presentationTracker =
                new EncodedFramePresentationTracker();
            private volatile boolean inputEnded;
            private volatile boolean closed;

            QueueingCallback(
                EncodedVideoDecodeRequest request,
                EncodedVideoSampleProvider sampleProvider,
                EncodedVideoCodecObserver observer,
                boolean decoderHdr10ProfileSupported) {
                this.request = request;
                this.sampleProvider = sampleProvider;
                this.observer = observer;
                this.decoderHdr10ProfileSupported = decoderHdr10ProfileSupported;
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
                    boolean rendered = outputRequiresFrameIdentity(
                        info.size,
                        info.flags);
                    frameSequence = rendered
                        ? presentationTracker.markOutputReleased(info.presentationTimeUs)
                        : OptionalLong.empty();
                    codec.releaseOutputBuffer(index, rendered);
                    if (rendered) {
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
                if (closed) return;
                try {
                    EncodedVideoOutputFormat output = outputFormat(format);
                    if (!outputFormatMatches(
                        request, output, decoderHdr10ProfileSupported)) {
                        observer.onError(new EncodedVideoOutputFormatMismatch(
                            "MediaCodec output format does not match the selected HDR10 mode."));
                        return;
                    }
                    observer.onOutputFormatChanged(output);
                } catch (RuntimeException failure) {
                    observer.onError(failure);
                }
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
