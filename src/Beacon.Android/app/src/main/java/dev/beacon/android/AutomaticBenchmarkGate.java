package dev.beacon.android;

final class AutomaticBenchmarkGate {
    private String inFlight;
    private String completed;
    private boolean enabled;

    synchronized void enable() {
        enabled = true;
    }

    synchronized void disable() {
        enabled = false;
    }

    synchronized boolean begin(String fingerprint) {
        String key = requireFingerprint(fingerprint);
        if (!enabled) return false;
        if (key.equals(inFlight) || key.equals(completed)) return false;
        inFlight = key;
        return true;
    }

    synchronized void finish(String fingerprint, boolean success) {
        String key = requireFingerprint(fingerprint);
        if (!key.equals(inFlight)) return;
        inFlight = null;
        if (success) completed = key;
    }

    private static String requireFingerprint(String value) {
        if (value == null || value.trim().isEmpty()) {
            throw new IllegalArgumentException("Benchmark fingerprint is required.");
        }
        return value.trim();
    }
}
