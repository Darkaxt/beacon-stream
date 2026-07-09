package dev.beacon.android;

import java.util.HashMap;
import java.util.Iterator;
import java.util.Map;

public final class RtpReorderingPacketSource implements RtpPacketSource {
    private final RtpPacketSource inner;
    private final int maxBufferedPackets;
    private final Map<Integer, RtpPacket> bufferedPackets = new HashMap<>();
    private boolean initialized;
    private int expectedSequence;
    private boolean innerEnded;
    private boolean closed;

    public RtpReorderingPacketSource(RtpPacketSource inner, int maxBufferedPackets) {
        if (inner == null) {
            throw new IllegalArgumentException("RTP packet source is required.");
        }

        if (maxBufferedPackets < 1) {
            throw new IllegalArgumentException("RTP reorder window must be at least one packet.");
        }

        this.inner = inner;
        this.maxBufferedPackets = maxBufferedPackets;
    }

    @Override
    public RtpPacket nextPacket() {
        if (closed) {
            return null;
        }

        RtpPacket expectedPacket = removeExpectedBufferedPacket();
        if (expectedPacket != null) {
            return emit(expectedPacket);
        }

        while (!innerEnded) {
            RtpPacket packet = inner.nextPacket();
            if (packet == null) {
                innerEnded = true;
                break;
            }

            if (!initialized) {
                initialized = true;
                return emit(packet);
            }

            int sequenceNumber = packet.sequenceNumber();
            if (sequenceNumber == expectedSequence) {
                return emit(packet);
            }

            if (isLateOrDuplicate(sequenceNumber, expectedSequence)) {
                continue;
            }

            if (!bufferedPackets.containsKey(sequenceNumber)) {
                bufferedPackets.put(sequenceNumber, packet);
            }

            expectedPacket = removeExpectedBufferedPacket();
            if (expectedPacket != null) {
                return emit(expectedPacket);
            }

            if (bufferedPackets.size() >= maxBufferedPackets) {
                return emit(removeNearestFutureBufferedPacket());
            }
        }

        return emitNextBufferedPacket();
    }

    @Override
    public void close() {
        if (closed) {
            return;
        }

        closed = true;
        bufferedPackets.clear();
        inner.close();
    }

    private RtpPacket emit(RtpPacket packet) {
        expectedSequence = nextSequence(packet.sequenceNumber());
        dropLateBufferedPackets();
        return packet;
    }

    private RtpPacket removeExpectedBufferedPacket() {
        return initialized ? bufferedPackets.remove(expectedSequence) : null;
    }

    private RtpPacket emitNextBufferedPacket() {
        if (bufferedPackets.isEmpty()) {
            return null;
        }

        return emit(removeNearestFutureBufferedPacket());
    }

    private RtpPacket removeNearestFutureBufferedPacket() {
        int nearestSequence = -1;
        int nearestDistance = 0x10000;
        for (Integer sequenceNumber : bufferedPackets.keySet()) {
            int distance = sequenceDistance(expectedSequence, sequenceNumber);
            if (distance < nearestDistance) {
                nearestDistance = distance;
                nearestSequence = sequenceNumber;
            }
        }

        return bufferedPackets.remove(nearestSequence);
    }

    private void dropLateBufferedPackets() {
        Iterator<Integer> iterator = bufferedPackets.keySet().iterator();
        while (iterator.hasNext()) {
            if (isLateOrDuplicate(iterator.next(), expectedSequence)) {
                iterator.remove();
            }
        }
    }

    private static int nextSequence(int sequenceNumber) {
        return (sequenceNumber + 1) & 0xFFFF;
    }

    private static int sequenceDistance(int fromInclusive, int toExclusive) {
        return (toExclusive - fromInclusive) & 0xFFFF;
    }

    private static boolean isLateOrDuplicate(int sequenceNumber, int expectedSequence) {
        return sequenceDistance(expectedSequence, sequenceNumber) >= 0x8000;
    }
}
