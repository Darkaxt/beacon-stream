package dev.beacon.android;

import org.junit.Test;

import java.util.ArrayDeque;
import java.util.Queue;

import static org.junit.Assert.assertArrayEquals;
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
    public void h264PlanUsesH264RtpSampleProvider() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource();
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("H.264 RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan("\"codec\":\"h264\""), sessionInfo());

        assertTrue(result.success());
        assertTrue(consumer.sampleProvider instanceof H264RtpSampleProvider);
    }

    @Test
    public void nonH264PlanKeepsRawRtpSampleProvider() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource();
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("raw RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan("\"codec\":\"hevc\""), sessionInfo());

        assertTrue(result.success());
        assertTrue(consumer.sampleProvider instanceof GameStreamRtpVideoSampleProvider);
    }

    @Test
    public void rawRtpProviderReceivesPacketsInSequenceOrder() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(10, 90000L, new byte[] {0x0A}),
            packet(12, 90000L, new byte[] {0x0C}),
            packet(11, 90000L, new byte[] {0x0B}));
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("raw RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan("\"codec\":\"hevc\""), sessionInfo());
        EncodedVideoSample first = consumer.sampleProvider.nextSample();
        EncodedVideoSample second = consumer.sampleProvider.nextSample();
        EncodedVideoSample third = consumer.sampleProvider.nextSample();

        assertTrue(result.success());
        assertArrayEquals(new byte[] {0x0A}, first.data());
        assertArrayEquals(new byte[] {0x0B}, second.data());
        assertArrayEquals(new byte[] {0x0C}, third.data());
    }

    @Test
    public void h264RtpProviderReceivesFragmentsInSequenceOrder() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0x7C, (byte) 0x85, 0x11}),
            packet(3, 90000L, new byte[] {0x7C, 0x45, 0x33}),
            packet(2, 90000L, new byte[] {0x7C, 0x05, 0x22}));
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("H.264 RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan("\"codec\":\"h264\""), sessionInfo());
        EncodedVideoSample sample = consumer.sampleProvider.nextSample();

        assertTrue(result.success());
        assertArrayEquals(new byte[] {0, 0, 0, 1, 0x65, 0x11, 0x22, 0x33}, sample.data());
    }

    @Test
    public void h264RtpProviderGroupsSameTimestampPacketsInSession() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, false, new byte[] {0x41, 0x11}),
            packet(2, 90000L, true, new byte[] {0x41, 0x22}));
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("H.264 RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan("\"codec\":\"h264\""), sessionInfo());
        EncodedVideoSample sample = consumer.sampleProvider.nextSample();

        assertTrue(result.success());
        assertArrayEquals(
            concat(
                start(), new byte[] {0x41, 0x11},
                start(), new byte[] {0x41, 0x22}),
            sample.data());
    }

    @Test
    public void h264PlanInjectsParameterSetsFromMetadata() {
        assertParameterSetMetadataInjects("h264SpropParameterSets");
    }

    @Test
    public void h264PlanAcceptsCamelCaseParameterSetAlias() {
        assertParameterSetMetadataInjects("spropParameterSets");
    }

    @Test
    public void h264PlanAcceptsSdpParameterSetAlias() {
        assertParameterSetMetadataInjects("sprop-parameter-sets");
    }

    @Test
    public void h264PlanFallsBackToRtspSdpParameterSets() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0x65, 0x11}));
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("H.264 RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(
            completePlan("\"codec\":\"h264\""),
            sessionInfoWithParameterSets("Z0IAHg==,aM4G4g=="));
        EncodedVideoSample sample = consumer.sampleProvider.nextSample();

        assertTrue(result.success());
        assertArrayEquals(
            concat(
                start(), new byte[] {0x67, 0x42, 0x00, 0x1E},
                start(), new byte[] {0x68, (byte) 0xCE, 0x06, (byte) 0xE2},
                start(), new byte[] {0x65, 0x11}),
            sample.data());
    }

    @Test
    public void h264DescriptorParameterSetsOverrideRtspSdpParameterSets() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0x65, 0x11}));
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("H.264 RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(
            completePlan("\"codec\":\"h264\",\"h264SpropParameterSets\":\"Z0IAHg==,aM4G4g==\""),
            sessionInfoWithParameterSets("Z0IAHw==,aM4G4w=="));
        EncodedVideoSample sample = consumer.sampleProvider.nextSample();

        assertTrue(result.success());
        assertArrayEquals(
            concat(
                start(), new byte[] {0x67, 0x42, 0x00, 0x1E},
                start(), new byte[] {0x68, (byte) 0xCE, 0x06, (byte) 0xE2},
                start(), new byte[] {0x65, 0x11}),
            sample.data());
    }

    @Test
    public void invalidH264ParameterSetMetadataFailsAndClosesSource() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0x65, 0x11}));
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("should not start"));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(
            completePlan("\"codec\":\"h264\",\"h264SpropParameterSets\":\"invalid\""),
            sessionInfo());

        assertFalse(result.success());
        assertEquals(
            "GameStream RTP video consumer failed: H.264 sprop-parameter-sets metadata is invalid.",
            result.diagnostic());
        assertEquals(0, consumer.startCount);
        assertEquals(1, source.closeCount);
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
    public void stopClosesSourceAndConsumerOnceAfterSuccessfulStart() {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource();
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(completePlan(), sessionInfo());
        client.stop();
        client.stop();

        assertTrue(result.success());
        assertEquals(1, source.closeCount);
        assertEquals(1, consumer.stopCount);
    }

    private static GameStreamEndpointPlan completePlan() {
        return completePlan("");
    }

    private static GameStreamEndpointPlan completePlan(String metadata) {
        StreamConnectionDescriptor descriptor = StreamConnectionDescriptor.extract(
            "{\"stream\":{\"connection\":{\"protocol\":\"gamestream\",\"endpoints\":[" +
                "{\"role\":\"rtsp\",\"uri\":\"rtsp://127.0.0.1:48010/beacon/session\"}," +
                "{\"role\":\"video\",\"uri\":\"udp://127.0.0.1:47998\"}," +
                "{\"role\":\"control\",\"uri\":\"tcp://127.0.0.1:47999\"}," +
                "{\"role\":\"audio\",\"uri\":\"udp://127.0.0.1:48000\"}],\"metadata\":{" +
                metadata +
                "}}}}");
        return GameStreamEndpointPlan.from(descriptor);
    }

    private static void assertParameterSetMetadataInjects(String metadataKey) {
        RecordingRtpPacketSource source = new RecordingRtpPacketSource(
            packet(1, 90000L, new byte[] {0x65, 0x11}));
        RecordingVideoConsumer consumer = new RecordingVideoConsumer(
            NativeStreamStartResult.started("H.264 RTP video sample provider started."));
        GameStreamRtpVideoSessionClient client = new GameStreamRtpVideoSessionClient(
            new RecordingPacketSourceFactory(source),
            consumer);

        NativeStreamStartResult result = client.start(
            completePlan("\"codec\":\"h264\",\"" + metadataKey + "\":\"Z0IAHg==,aM4G4g==\""),
            sessionInfo());
        EncodedVideoSample sample = consumer.sampleProvider.nextSample();

        assertTrue(result.success());
        assertArrayEquals(
            concat(
                start(), new byte[] {0x67, 0x42, 0x00, 0x1E},
                start(), new byte[] {0x68, (byte) 0xCE, 0x06, (byte) 0xE2},
                start(), new byte[] {0x65, 0x11}),
            sample.data());
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

    private static GameStreamRtspSessionInfo sessionInfoWithParameterSets(String h264SpropParameterSets) {
        return GameStreamRtspSessionInfo.startedWithClientPorts(
            "gamestream",
            "rtsp://127.0.0.1:48010/beacon/session",
            "session-1",
            50000,
            48000,
            50002,
            47998,
            50004,
            47999,
            null,
            h264SpropParameterSets);
    }

    private static RtpPacket packet(int sequenceNumber, long timestamp, byte[] payload) {
        return packet(sequenceNumber, timestamp, false, payload);
    }

    private static RtpPacket packet(int sequenceNumber, long timestamp, boolean marker, byte[] payload) {
        byte[] bytes = new byte[12 + payload.length];
        bytes[0] = (byte) 0x80;
        bytes[1] = (byte) (0x60 | (marker ? 0x80 : 0));
        bytes[2] = (byte) ((sequenceNumber >>> 8) & 0xFF);
        bytes[3] = (byte) (sequenceNumber & 0xFF);
        bytes[4] = (byte) ((timestamp >>> 24) & 0xFF);
        bytes[5] = (byte) ((timestamp >>> 16) & 0xFF);
        bytes[6] = (byte) ((timestamp >>> 8) & 0xFF);
        bytes[7] = (byte) (timestamp & 0xFF);
        bytes[11] = 0x01;
        System.arraycopy(payload, 0, bytes, 12, payload.length);
        return RtpPacket.parse(bytes);
    }

    private static byte[] start() {
        return new byte[] {0, 0, 0, 1};
    }

    private static byte[] concat(byte[]... chunks) {
        int total = 0;
        for (byte[] chunk : chunks) {
            total += chunk.length;
        }

        byte[] result = new byte[total];
        int offset = 0;
        for (byte[] chunk : chunks) {
            System.arraycopy(chunk, 0, result, offset, chunk.length);
            offset += chunk.length;
        }

        return result;
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
        private int stopCount;

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

        @Override
        public void stop() {
            stopCount++;
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
        private final Queue<RtpPacket> packets = new ArrayDeque<>();
        private int closeCount;

        private RecordingRtpPacketSource(RtpPacket... packets) {
            for (RtpPacket packet : packets) {
                this.packets.add(packet);
            }
        }

        @Override
        public RtpPacket nextPacket() {
            return packets.poll();
        }

        @Override
        public void close() {
            closeCount++;
        }
    }
}
