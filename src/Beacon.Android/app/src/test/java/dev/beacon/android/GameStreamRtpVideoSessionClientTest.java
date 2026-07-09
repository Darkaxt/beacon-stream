package dev.beacon.android;

import org.junit.Test;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertSame;
import static org.junit.Assert.assertTrue;

public final class GameStreamRtpVideoSessionClientTest {
    @Test
    public void startsConsumerWithRtpSampleProvider() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource();
        RecordingPacketSourceFactory sourceFactory = new RecordingPacketSourceFactory(source);
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(sourceFactory, consumer);
        GameStreamEndpointPlan plan = completePlan();
        GameStreamRtspSessionInfo sessionInfo = sessionInfo();

        NativeStreamStartResult result = client.start(plan, sessionInfo);

        assertTrue(result.success());
        assertEquals("RTP video sample provider started.", result.status());
        assertSame(plan, sourceFactory.requestedPlan);
        assertSame(sessionInfo, sourceFactory.requestedSessionInfo);
        assertSame(plan, consumer.requestedPlan);
        assertSame(sessionInfo, consumer.requestedSessionInfo);
        assertTrue(consumer.sampleProvider instanceof GameStreamRtpVideoSampleProvider);
        assertEquals(0, source.closeCount);
    }

    @Test
    public void sourceFactoryFailureReturnsDiagnostic() {
        ThrowingPacketSourceFactory sourceFactory = new ThrowingPacketSourceFactory();
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("should not start"));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(sourceFactory, consumer);

        NativeStreamStartResult result = client.start(completePlan(), sessionInfo());

        assertFalse(result.success());
        assertEquals("GameStream RTP video source failed: udp source failed", result.diagnostic());
        assertEquals(0, consumer.startCount);
    }

    @Test
    public void consumerFailureClosesSource() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource();
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.unsupported("decoder rejected RTP samples"));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan(), sessionInfo());

        assertFalse(result.success());
        assertEquals("decoder rejected RTP samples", result.diagnostic());
        assertEquals(1, source.closeCount);
    }

    @Test
    public void consumerExceptionReturnsDiagnosticAndClosesSource() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource();
        ThrowingVideoConsumer consumer = new ThrowingVideoConsumer();
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan(), sessionInfo());

        assertFalse(result.success());
        assertEquals("GameStream RTP video consumer failed: decoder exploded", result.diagnostic());
        assertEquals(1, source.closeCount);
        assertEquals(1, consumer.startCount);
    }

    @Test
    public void stopClosesSourceOnceAfterSuccessfulStart() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource();
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            new RecordingVideoConsumer(NativeStreamStartResult.started("RTP video sample provider started.")));

        NativeStreamStartResult result = client.start(completePlan(), sessionInfo());
        client.stop();
        client.stop();

        assertTrue(result.success());
        assertEquals(1, source.closeCount);
    }

    private static GameStreamEndpointPlan completePlan() {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}]}}}");
        return GameStreamEndpointPlan.from(descriptor);
    }

    private static GameStreamRtspSessionInfo sessionInfo() {
        return GameStreamRtspSessionInfo.started(
            "gamestream",
            "rtsp://127.0.0.1:48010/beacon/session",
            "session-1",
            48000,
            47998,
            47999);
    }

    private static final class RecordingPacketSourceFactory implements GameStreamRtpPacketSourceFactory {
        private final RtpPacketSource source;
        private GameStreamEndpointPlan requestedPlan;
        private GameStreamRtspSessionInfo requestedSessionInfo;

        private RecordingPacketSourceFactory(RtpPacketSource source) {
            this.source = source;
        }

        @Override
        public RtpPacketSource create(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo) {
            requestedPlan = plan;
            requestedSessionInfo = sessionInfo;
            return source;
        }
    }

    private static final class ThrowingPacketSourceFactory implements GameStreamRtpPacketSourceFactory {
        @Override
        public RtpPacketSource create(GameStreamEndpointPlan plan, GameStreamRtspSessionInfo sessionInfo) {
            throw new IllegalStateException("udp source failed");
        }
    }

    private static final class RecordingVideoConsumer implements GameStreamRtpVideoConsumer {
        private final NativeStreamStartResult result;
        private GameStreamEndpointPlan requestedPlan;
        private GameStreamRtspSessionInfo requestedSessionInfo;
        private EncodedVideoSampleProvider sampleProvider;
        private int startCount;

        private RecordingVideoConsumer(NativeStreamStartResult result) {
            this.result = result;
        }

        @Override
        public NativeStreamStartResult start(
            GameStreamEndpointPlan plan,
            GameStreamRtspSessionInfo sessionInfo,
            EncodedVideoSampleProvider sampleProvider) {
            startCount++;
            requestedPlan = plan;
            requestedSessionInfo = sessionInfo;
            this.sampleProvider = sampleProvider;
            return result;
        }
    }

    private static final class ThrowingVideoConsumer implements GameStreamRtpVideoConsumer {
        private int startCount;

        @Override
        public NativeStreamStartResult start(
            GameStreamEndpointPlan plan,
            GameStreamRtspSessionInfo sessionInfo,
            EncodedVideoSampleProvider sampleProvider) {
            startCount++;
            throw new IllegalStateException("decoder exploded");
        }
    }

    private static final class RecordingRtpPacketSource implements RtpPacketSource {
        private int closeCount;

        @Override
        public RtpPacket nextPacket() {
            return null;
        }

        @Override
        public void close() {
            closeCount++;
        }
    }
}
