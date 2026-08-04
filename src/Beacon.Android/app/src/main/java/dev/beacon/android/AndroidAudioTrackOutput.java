package dev.beacon.android;

import android.media.AudioAttributes;
import android.media.AudioFormat;
import android.media.AudioTrack;

import java.nio.ByteBuffer;

final class AndroidAudioTrackOutput implements BeaconAudioSession.AudioOutput {
    private static final int OPUS_FRAME_BYTES = 1_920 * Float.BYTES;

    private final AudioTrack track;
    private boolean stopped;
    private boolean released;

    AndroidAudioTrackOutput(int sampleRateHz, int channelCount) {
        if (sampleRateHz != 48_000 || channelCount != 2) {
            throw new IllegalArgumentException(
                "Beacon AudioTrack requires 48 kHz stereo PCM.");
        }
        AudioFormat format = new AudioFormat.Builder()
            .setEncoding(AudioFormat.ENCODING_PCM_FLOAT)
            .setSampleRate(sampleRateHz)
            .setChannelMask(AudioFormat.CHANNEL_OUT_STEREO)
            .build();
        int minimumBytes = AudioTrack.getMinBufferSize(
            sampleRateHz,
            AudioFormat.CHANNEL_OUT_STEREO,
            AudioFormat.ENCODING_PCM_FLOAT);
        if (minimumBytes <= 0) {
            throw new IllegalStateException(
                "Android did not provide a valid PCM float buffer size.");
        }
        track = new AudioTrack.Builder()
            .setAudioAttributes(new AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_GAME)
                .setContentType(AudioAttributes.CONTENT_TYPE_MUSIC)
                .build())
            .setAudioFormat(format)
            .setTransferMode(AudioTrack.MODE_STREAM)
            .setBufferSizeInBytes(Math.max(minimumBytes, OPUS_FRAME_BYTES * 4))
            .build();
        if (track.getState() != AudioTrack.STATE_INITIALIZED) {
            track.release();
            released = true;
            throw new IllegalStateException("Android AudioTrack did not initialize.");
        }
    }

    @Override
    public void play() {
        if (released) throw new IllegalStateException("Beacon AudioTrack is released.");
        track.play();
    }

    @Override
    public void write(ByteBuffer pcm) {
        if (released || stopped) {
            throw new IllegalStateException("Beacon AudioTrack is not playing.");
        }
        ByteBuffer remaining = pcm.asReadOnlyBuffer();
        while (remaining.hasRemaining()) {
            int written = track.write(
                remaining, remaining.remaining(), AudioTrack.WRITE_BLOCKING);
            if (written <= 0) {
                throw new IllegalStateException(
                    "Android AudioTrack write failed with code " + written + ".");
            }
        }
    }

    @Override
    public void stop() {
        if (released || stopped) return;
        stopped = true;
        track.stop();
    }

    @Override
    public void release() {
        if (released) return;
        released = true;
        track.release();
    }
}
