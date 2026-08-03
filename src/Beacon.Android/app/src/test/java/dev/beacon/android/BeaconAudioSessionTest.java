package dev.beacon.android;

import org.junit.Test;

import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotNull;
import static org.junit.Assert.assertSame;

public final class BeaconAudioSessionTest {
    @Test
    public void writesPcmOnDedicatedExecutorAndReleasesOutputExactlyOnce()
        throws Exception {
        ExecutorService executor = Executors.newSingleThreadExecutor(
            action -> new Thread(action, "beacon-audio-test"));
        RecordingOutput output = new RecordingOutput();
        BeaconAudioSession session = new BeaconAudioSession(
            executor,
            (sampleRateHz, channelCount) -> {
                assertEquals(48_000, sampleRateHz);
                assertEquals(2, channelCount);
                return output;
            },
            failure -> { throw new AssertionError(failure); });

        session.start(7, selectedAudio());
        session.onAudioPcm(decodedFrame(1, 0.25F));

        output.written.await();
        assertEquals("beacon-audio-test", output.writeThread.get());
        assertEquals(1, output.playCount);
        assertEquals(1, output.writeCount);
        assertEquals(0.25F, output.firstSample, 0.0001F);
        session.stop();
        session.stop();
        session.close();
        assertEquals(1, output.stopCount);
        assertEquals(1, output.releaseCount);
    }

    @Test
    public void replacementGenerationReleasesOldOutputBeforePlayingNewOutput() {
        ExecutorService executor = Executors.newSingleThreadExecutor();
        RecordingOutput first = new RecordingOutput();
        RecordingOutput second = new RecordingOutput();
        int[] created = { 0 };
        BeaconAudioSession session = new BeaconAudioSession(
            executor,
            (sampleRateHz, channelCount) -> created[0]++ == 0 ? first : second,
            failure -> { throw new AssertionError(failure); });

        session.start(1, selectedAudio());
        session.start(2, selectedAudio());

        assertEquals(1, first.stopCount);
        assertEquals(1, first.releaseCount);
        assertEquals(1, second.playCount);
        session.close();
        assertEquals(1, second.stopCount);
        assertEquals(1, second.releaseCount);
    }

    @Test
    public void framesAfterStopAreIgnored() {
        ExecutorService executor = Executors.newSingleThreadExecutor();
        RecordingOutput output = new RecordingOutput();
        BeaconAudioSession session = new BeaconAudioSession(
            executor,
            (sampleRateHz, channelCount) -> output,
            failure -> { throw new AssertionError(failure); });
        session.start(1, selectedAudio());
        session.stop();

        session.onAudioPcm(decodedFrame(2, 0.5F));
        session.close();

        assertEquals(0, output.writeCount);
    }

    @Test
    public void writeFailureIsReportedWhenCleanupAlsoFails() throws Exception {
        ExecutorService executor = Executors.newSingleThreadExecutor();
        RuntimeException writeFailure = new RuntimeException("write failed");
        RuntimeException cleanupFailure = new RuntimeException("stop failed");
        AtomicReference<Throwable> observedFailure = new AtomicReference<>();
        int[] releaseCount = { 0 };
        BeaconAudioSession.AudioOutput output = new BeaconAudioSession.AudioOutput() {
            @Override public void play() { }

            @Override public void write(ByteBuffer pcm) { throw writeFailure; }

            @Override public void stop() { throw cleanupFailure; }

            @Override public void release() { releaseCount[0]++; }
        };
        BeaconAudioSession session = new BeaconAudioSession(
            executor,
            (sampleRateHz, channelCount) -> output,
            observedFailure::set);

        session.start(1, selectedAudio());
        session.onAudioPcm(decodedFrame(1, 0.5F));
        executor.submit(() -> { }).get();

        assertSame(writeFailure, observedFailure.get());
        assertEquals(1, writeFailure.getSuppressed().length);
        assertSame(cleanupFailure, writeFailure.getSuppressed()[0]);
        assertEquals(1, releaseCount[0]);
        session.close();
    }

    private static BeaconStreamSession.SelectedAudio selectedAudio() {
        return new BeaconStreamSession.SelectedAudio(
            "opus", 48_000, 2, 20_000, 96_000);
    }

    private static BeaconStreamCore.DecodedAudioFrame decodedFrame(
        long sequence, float firstSample) {
        ByteBuffer pcm = ByteBuffer.allocateDirect(1_920 * Float.BYTES)
            .order(ByteOrder.nativeOrder());
        pcm.putFloat(firstSample);
        pcm.position(0);
        return new BeaconStreamCore.DecodedAudioFrame(
            pcm, sequence * 20_000, sequence, false);
    }

    private static final class RecordingOutput
        implements BeaconAudioSession.AudioOutput {
        final CountDownLatch written = new CountDownLatch(1);
        final AtomicReference<String> writeThread = new AtomicReference<>();
        int playCount;
        int writeCount;
        int stopCount;
        int releaseCount;
        float firstSample;

        @Override public void play() { playCount++; }

        @Override public void write(ByteBuffer pcm) {
            assertNotNull(pcm);
            writeThread.set(Thread.currentThread().getName());
            firstSample = pcm.order(ByteOrder.nativeOrder()).getFloat();
            writeCount++;
            written.countDown();
        }

        @Override public void stop() { stopCount++; }

        @Override public void release() { releaseCount++; }
    }
}
