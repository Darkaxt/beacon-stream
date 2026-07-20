package dev.beacon.android;

import android.content.Context;
import android.content.SharedPreferences;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.File;
import java.io.FileOutputStream;
import java.io.IOException;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Arrays;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.atomic.AtomicLong;
import java.util.concurrent.atomic.AtomicReference;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNotEquals;
import static org.junit.Assert.assertTrue;

final class Gate3SessionEvidence implements BeaconVideoFeedbackBridge.Observer {
    private static final String PreferencesName = "beacon.gate3.session.evidence";
    private static final String TicketFingerprintKey = "ticketFingerprint";
    private static final String SessionIdKey = "sessionId";
    private static final String FrameSequenceKey = "frameSequence";
    private static final String TicketEvidenceFile = "beacon-gate3-ticket-evidence";

    private final SharedPreferences preferences;
    private final File ticketEvidence;
    private final CountDownLatch frameRendered = new CountDownLatch(1);
    private final CountDownLatch framePresented = new CountDownLatch(1);
    private final AtomicLong renderedFrameCount = new AtomicLong();
    private final AtomicReference<Throwable> failure = new AtomicReference<>();
    private boolean transportConnected;
    private boolean inputSent;
    private boolean transportClosed;
    private boolean renderedFeedbackObserved;
    private boolean surfacePresentationObserved;
    private long frameSequence;
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
        long frameSequence = preferences.getLong(FrameSequenceKey, 0);
        if (ticketFingerprint == null || sessionId == null || frameSequence <= 0) {
            throw new IllegalStateException("Missing Gate 3 reconnect evidence.");
        }
        return new PreviousInvocation(ticketFingerprint, sessionId, frameSequence);
    }

    @Override
    public void onRenderedFrameSent(
        long generation,
        long renderedFrameSequence,
        long presentationTimeUs,
        long renderedAtUs) {
        renderedFrameCount.incrementAndGet();
        if (generation <= 0 || renderedFrameSequence <= 0 ||
            presentationTimeUs < 0 || renderedAtUs < 0) {
            failure.compareAndSet(null,
                new AssertionError("Rendered Gate 3 frame evidence is invalid."));
        }
        if (frameSequence == 0) frameSequence = renderedFrameSequence;
        transportConnected = true;
        renderedFeedbackObserved = true;
        frameRendered.countDown();
    }

    @Override
    public void onDecoderStateSent(
        long generation,
        BeaconStreamCore.DecoderState state,
        int platformErrorCode) {
        if (state == BeaconStreamCore.DecoderState.FAILED) {
            recordStreamFailure(
                "decoder:" + platformErrorCode + ":generation:" + generation);
        }
    }

    void recordSurfacePresentation(long presentationTimeUs, long renderedAtNs) {
        if (presentationTimeUs < 0 || renderedAtNs <= 0) {
            failure.compareAndSet(null,
                new AssertionError("Gate 3 presentation-surface evidence is invalid."));
        }
        surfacePresentationObserved = true;
        framePresented.countDown();
    }

    void recordVideoFailure(Throwable videoFailure) {
        String message = videoFailure == null || videoFailure.getMessage() == null
            ? "unknown"
            : videoFailure.getMessage();
        recordStreamFailure("video:" + message);
    }

    void recordStreamFailure(String stage) {
        if (frameRendered.getCount() == 0 && framePresented.getCount() == 0) {
            return;
        }
        failure.compareAndSet(null,
            new AssertionError("Gate 3 stream failed before evidence completed: " + stage));
        frameRendered.countDown();
        framePresented.countDown();
    }

    void recordGrant(String responseBody) {
        JsonObject connection = JsonParser.parseString(responseBody)
            .getAsJsonObject().getAsJsonObject("connection");
        sessionId = connection.get("sessionId").getAsString();
        String ticket = connection.get("ticket").getAsString();
        ticketFingerprint = fingerprint(ticket);
        appendTicketEvidence(ticket);
    }

    void awaitRenderedFrameFeedback() throws InterruptedException {
        frameRendered.await();
        framePresented.await();
        Throwable observed = failure.get();
        if (observed instanceof AssertionError) {
            throw (AssertionError) observed;
        }
        if (observed != null) {
            throw new AssertionError("Gate 3 video evidence failed.", observed);
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
        assertTrue("Gate 3 rendered frame sequence was not observed.", frameSequence > 0);
        assertTrue("Could not persist Gate 3 reconnect evidence.", preferences.edit()
            .putString(TicketFingerprintKey, ticketFingerprint)
            .putString(SessionIdKey, sessionId)
            .putLong(FrameSequenceKey, frameSequence)
            .commit());
    }

    void assertFreshReconnect(PreviousInvocation previous) {
        requireGrantEvidence();
        assertEquals(previous.sessionId, sessionId);
        assertNotEquals(previous.ticketFingerprint, ticketFingerprint);
        assertTrue("Reconnect did not render a frame.", frameSequence > 0);
    }

    void clearPersistedReconnect() {
        assertTrue("Could not clear Gate 3 reconnect evidence.", preferences.edit()
            .remove(TicketFingerprintKey)
            .remove(SessionIdKey)
            .remove(FrameSequenceKey)
            .commit());
    }

    boolean transportConnected() {
        return transportConnected;
    }

    long receivedFrameCount() {
        return renderedFrameCount.get();
    }

    long frameSequence() {
        return frameSequence;
    }

    boolean inputSent() {
        return inputSent;
    }

    boolean feedbackSent() {
        return renderedFeedbackObserved;
    }

    boolean surfacePresented() {
        return surfacePresentationObserved;
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
        final long frameSequence;

        PreviousInvocation(String ticketFingerprint, String sessionId, long frameSequence) {
            this.ticketFingerprint = ticketFingerprint;
            this.sessionId = sessionId;
            this.frameSequence = frameSequence;
        }
    }
}
