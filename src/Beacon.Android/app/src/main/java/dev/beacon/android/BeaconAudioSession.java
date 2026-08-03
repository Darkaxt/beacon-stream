package dev.beacon.android;

import java.nio.ByteBuffer;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;

final class BeaconAudioSession
    implements BeaconViewModel.AudioSession {
    private final ExecutorService executor;
    private final AudioOutputFactory outputFactory;
    private final FailureObserver failureObserver;
    private AudioOutput output;
    private long activeGeneration;
    private boolean closed;

    BeaconAudioSession(FailureObserver failureObserver) {
        this(
            Executors.newSingleThreadExecutor(
                action -> new Thread(action, "beacon-audio-playback")),
            AndroidAudioTrackOutput::new,
            failureObserver);
    }

    BeaconAudioSession(
        ExecutorService executor,
        AudioOutputFactory outputFactory,
        FailureObserver failureObserver) {
        if (executor == null || outputFactory == null || failureObserver == null) {
            throw new IllegalArgumentException("Beacon audio dependencies are required.");
        }
        this.executor = executor;
        this.outputFactory = outputFactory;
        this.failureObserver = failureObserver;
    }

    @Override
    public void start(long generation, BeaconStreamSession.SelectedAudio audio) {
        validateStart(generation, audio);
        AudioOutput previous;
        synchronized (this) {
            if (closed) throw new IllegalStateException("Beacon audio session is closed.");
            if (activeGeneration == generation && output != null) return;
            activeGeneration = 0;
            previous = output;
            output = null;
        }

        AudioOutput replacement;
        try {
            replacement = await(executor.submit(() -> {
                closeOutput(previous);
                AudioOutput created = outputFactory.create(
                    audio.sampleRateHz(), audio.channelCount());
                if (created == null) {
                    throw new IllegalStateException("Beacon audio output is unavailable.");
                }
                try {
                    created.play();
                    return created;
                } catch (RuntimeException | Error failure) {
                    closeOutput(created);
                    throw failure;
                }
            }));
        } catch (RuntimeException | Error failure) {
            reportFailure(failure);
            throw failure;
        }

        synchronized (this) {
            if (closed) {
                await(executor.submit(() -> closeOutput(replacement)));
                throw new IllegalStateException("Beacon audio session is closed.");
            }
            output = replacement;
            activeGeneration = generation;
        }
    }

    @Override
    public void onAudioPcm(BeaconStreamCore.DecodedAudioFrame frame) {
        if (frame == null) return;
        AudioOutput owned;
        long generation;
        synchronized (this) {
            if (closed || activeGeneration == 0 || output == null) return;
            owned = output;
            generation = activeGeneration;
        }
        executor.execute(() -> writeIfCurrent(owned, generation, frame));
    }

    @Override
    public void stop() {
        AudioOutput owned;
        synchronized (this) {
            if (closed || (activeGeneration == 0 && output == null)) return;
            activeGeneration = 0;
            owned = output;
            output = null;
        }
        await(executor.submit(() -> closeOutput(owned)));
    }

    @Override
    public void close() {
        AudioOutput owned;
        synchronized (this) {
            if (closed) return;
            closed = true;
            activeGeneration = 0;
            owned = output;
            output = null;
        }
        try {
            await(executor.submit(() -> closeOutput(owned)));
        } finally {
            executor.shutdown();
        }
    }

    private void writeIfCurrent(
        AudioOutput owned,
        long generation,
        BeaconStreamCore.DecodedAudioFrame frame) {
        synchronized (this) {
            if (closed || output != owned || activeGeneration != generation) return;
        }
        try {
            owned.write(frame.pcm.asReadOnlyBuffer());
        } catch (RuntimeException | Error failure) {
            boolean release;
            synchronized (this) {
                release = output == owned && activeGeneration == generation;
                if (release) {
                    output = null;
                    activeGeneration = 0;
                }
            }
            Throwable reportedFailure = failure;
            if (release) {
                try {
                    closeOutput(owned);
                } catch (RuntimeException | Error cleanupFailure) {
                    reportedFailure.addSuppressed(cleanupFailure);
                }
            }
            reportFailure(reportedFailure);
        }
    }

    private static void validateStart(
        long generation, BeaconStreamSession.SelectedAudio audio) {
        if (generation <= 0 || audio == null || !"opus".equals(audio.codec()) ||
            audio.sampleRateHz() != 48_000 || audio.channelCount() != 2 ||
            audio.frameDurationUs() != 20_000) {
            throw new IllegalArgumentException(
                "Beacon audio requires Opus 48 kHz stereo 20 ms frames.");
        }
    }

    private static void closeOutput(AudioOutput output) {
        if (output == null) return;
        Throwable failure = null;
        try {
            output.stop();
        } catch (RuntimeException | Error error) {
            failure = error;
        }
        try {
            output.release();
        } catch (RuntimeException | Error error) {
            if (failure == null) failure = error;
            else failure.addSuppressed(error);
        }
        if (failure instanceof RuntimeException runtimeFailure) {
            throw runtimeFailure;
        }
        if (failure instanceof Error error) throw error;
    }

    private void reportFailure(Throwable failure) {
        try {
            failureObserver.onFailure(failure);
        } catch (RuntimeException | Error ignored) {
        }
    }

    private static <T> T await(Future<T> future) {
        boolean interrupted = false;
        try {
            while (true) {
                try {
                    return future.get();
                } catch (InterruptedException failure) {
                    interrupted = true;
                } catch (ExecutionException failure) {
                    Throwable cause = failure.getCause();
                    if (cause instanceof RuntimeException runtimeFailure) {
                        throw runtimeFailure;
                    }
                    if (cause instanceof Error error) throw error;
                    throw new IllegalStateException("Beacon audio task failed.", cause);
                }
            }
        } finally {
            if (interrupted) Thread.currentThread().interrupt();
        }
    }

    interface AudioOutputFactory {
        AudioOutput create(int sampleRateHz, int channelCount);
    }

    interface AudioOutput {
        void play();
        void write(ByteBuffer pcm);
        void stop();
        void release();
    }

    interface FailureObserver {
        void onFailure(Throwable failure);
    }
}
