package dev.beacon.android;

import android.content.Context;
import android.content.SharedPreferences;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.nio.ByteBuffer;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Arrays;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

final class Gate3SessionEvidence implements BeaconStreamCore.EncodedFrameSink {
    private static final String PreferencesName = "beacon.gate3.session.evidence";
    private static final String TicketFingerprintKey = "ticketFingerprint";
    private static final String SessionIdKey = "sessionId";
    private static final String MarkerSequenceKey = "markerSequence";
    private static final String TicketEvidenceFile = "beacon-gate3-ticket-evidence";
    private static final byte[] MarkerBytes =
        "BEACON-G3-MARKER".getBytes(StandardCharsets.US_ASCII);

    private final SharedPreferences preferences;
    private final File ticketEvidence;
    private final CountDownLatch markerReceived = new CountDownLatch(1);
    private final CountDownLatch feedbackSent = new CountDownLatch(1);
    private final AtomicLong receivedFrameCount = new AtomicLong();
    private final AtomicReference<Throwable> failure = new AtomicReference<>();
    private boolean transportConnected;
    private boolean inputSent;
    private boolean transportClosed;
    private boolean feedbackObserved;
    private long markerSequence;
    private String ticketFingerprint;
    private String sessionId;

    private Gate3SessionEvidence(Context context, String clientId) {
        preferences = context.getSharedPreferences(
            PreferencesName + "." + clientId, Context.MODE_PRIVATE);
        ticketEvidence = new File(context.getFilesDir(), TicketEvidenceFile);
    }

    static Gate3SessionEvidence startFirstInvocation(Context context, String clientId) {
        Gate3SessionEvidence evidence = new Gate3SessionEvidence(context, clientId);
        evidence.clearPersistedReconnect();
        if (evidence.ticketEvidence.exists() && !evidence.ticketEvidence.delete()) {
            throw new IllegalStateException("Could not clear Gate 3 ticket evidence.");
        }
        return evidence;
    }

    static Gate3SessionEvidence startReconnect(Context context, String clientId) {
        return new Gate3SessionEvidence(context, clientId);
    }

    static PreviousInvocation loadPrevious(Context context, String clientId) {
        SharedPreferences preferences = context.getSharedPreferences(
            PreferencesName + "." + clientId, Context.MODE_PRIVATE);
        String ticketFingerprint = preferences.getString(TicketFingerprintKey, null);
        String sessionId = preferences.getString(SessionIdKey, null);
        long markerSequence = preferences.getLong(MarkerSequenceKey, 0);
        if (ticketFingerprint == null || sessionId == null || markerSequence <= 0) {
            throw new IllegalStateException("Missing Gate 3 reconnect evidence.");
        }
        return new PreviousInvocation(ticketFingerprint, sessionId, markerSequence);
    }

    @Override
    public void onFrame(BeaconStreamCore.EncodedFrame frame) {
        long count = receivedFrameCount.incrementAndGet();
        if (!matches(MarkerBytes, frame.bytes)) {
            failure.compareAndSet(null,
                new AssertionError("Expected the Gate 3 non-decodable marker bytes."));
        }
        if (frame.sequence <= 0) {
            failure.compareAndSet(null,
                new AssertionError("Expected a positive transport marker sequence."));
        }
        if (count != 1) {
            failure.compareAndSet(null,
                new AssertionError("Expected exactly one Gate 3 marker frame."));
        }
        markerSequence = frame.sequence;
        transportConnected = true;
        markerReceived.countDown();
    }

    private static boolean matches(byte[] expected, ByteBuffer actual) {
        ByteBuffer copy = actual.asReadOnlyBuffer();
        if (copy.remaining() != expected.length) return false;
        for (byte value : expected) {
            if (copy.get() != value) return false;
        }
        return true;
    }

    void recordFeedbackSent() {
        feedbackObserved = true;
        feedbackSent.countDown();
    }

    void recordStreamFailure(String stage) {
        if (markerReceived.getCount() == 0 && feedbackSent.getCount() == 0) {
            return;
        }
        failure.compareAndSet(null,
            new AssertionError("Gate 3 stream failed before evidence completed: " + stage));
        markerReceived.countDown();
        feedbackSent.countDown();
    }

    void recordGrant(String responseBody) {
        JsonObject connection = JsonParser.parseString(responseBody)
            .getAsJsonObject().getAsJsonObject("connection");
        sessionId = connection.get("sessionId").getAsString();
        String ticket = connection.get("ticket").getAsString();
        ticketFingerprint = fingerprint(ticket);
        appendTicketEvidence(ticket);
    }

    void awaitMarkerAndFeedback() throws InterruptedException {
        markerReceived.await();
        feedbackSent.await();
        Throwable observed = failure.get();
        if (observed instanceof AssertionError) {
            throw (AssertionError) observed;
        }
        if (observed != null) {
            throw new AssertionError("Gate 3 marker sink failed.", observed);
        }
    }

    void recordInputSent() {
        inputSent = true;
    }

    void recordTransportClosedAfterNativeDrain() {
        transportClosed = true;
    }

    void persistForReconnect() {
        requireGrantEvidence();
        assertTrue("Gate 3 marker sequence was not observed.", markerSequence > 0);
        assertTrue("Could not persist Gate 3 reconnect evidence.", preferences.edit()
            .putString(TicketFingerprintKey, ticketFingerprint)
            .putString(SessionIdKey, sessionId)
            .putLong(MarkerSequenceKey, markerSequence)
            .commit());
    }

    void assertFreshReconnect(PreviousInvocation previous) {
        requireGrantEvidence();
        assertEquals(previous.sessionId, sessionId);
        assertNotEquals(previous.ticketFingerprint, ticketFingerprint);
        assertEquals(previous.markerSequence + 1, markerSequence);
    }

    void clearPersistedReconnect() {
        assertTrue("Could not clear Gate 3 reconnect evidence.", preferences.edit()
            .remove(TicketFingerprintKey)
            .remove(SessionIdKey)
            .remove(MarkerSequenceKey)
            .commit());
    }

    boolean transportConnected() {
        return transportConnected;
    }

    long receivedFrameCount() {
        return receivedFrameCount.get();
    }

    long markerSequence() {
        return markerSequence;
    }

    boolean inputSent() {
        return inputSent;
    }

    boolean feedbackSent() {
        return feedbackObserved;
    }

    boolean transportClosed() {
        return transportClosed;
    }

    private void requireGrantEvidence() {
        if (ticketFingerprint == null || sessionId == null) {
            throw new IllegalStateException("Gate 3 connection grant evidence is incomplete.");
        }
    }

    private static String fingerprint(String ticket) {
        byte[] ticketBytes = ticket.getBytes(StandardCharsets.UTF_8);
        try {
            byte[] digest = MessageDigest.getInstance("SHA-256").digest(ticketBytes);
            try {
                StringBuilder rendered = new StringBuilder(digest.length * 2);
                for (byte value : digest) {
                    rendered.append(String.format("%02X", value));
                }
                return rendered.toString();
            } finally {
                Arrays.fill(digest, (byte) 0);
            }
        } catch (NoSuchAlgorithmException error) {
            throw new AssertionError("SHA-256 is unavailable.", error);
        } finally {
            Arrays.fill(ticketBytes, (byte) 0);
        }
    }

    private void appendTicketEvidence(String ticket) {
        byte[] bytes = ticket.getBytes(StandardCharsets.UTF_8);
        try (FileOutputStream output = new FileOutputStream(ticketEvidence, true)) {
            output.write(bytes);
            output.write('\n');
        } catch (IOException error) {
            throw new AssertionError("Could not write private Gate 3 ticket evidence.", error);
        } finally {
            Arrays.fill(bytes, (byte) 0);
        }
    }

    static final class PreviousInvocation {
        final String ticketFingerprint;
        final String sessionId;
        final long markerSequence;

        PreviousInvocation(String ticketFingerprint, String sessionId, long markerSequence) {
            this.ticketFingerprint = ticketFingerprint;
            this.sessionId = sessionId;
            this.markerSequence = markerSequence;
        }
    }
}
