package dev.beacon.android;

public final class BeaconPresenceCoordinator {
    private final BeaconEnrollmentStore enrollmentStore;
    private BeaconClientConfig enrolledConfig;
    private ActivationAttempt attemptedActivation;
    private boolean automaticPresenceEnabled;
    private boolean foreground;
    private boolean foregroundActivationClaimed;
    private boolean backgroundReconciliationClaimed;
    private boolean lifecycleActivationAttempted;

    public BeaconPresenceCoordinator(BeaconEnrollmentStore enrollmentStore) {
        if (enrollmentStore == null) {
            throw new IllegalArgumentException("Beacon enrollment store is required.");
        }
        this.enrollmentStore = enrollmentStore;
        enrolledConfig = enrollmentStore.load();
        automaticPresenceEnabled = enrolledConfig != null;
    }

    public synchronized BeaconClientConfig enrolledConfig() {
        return enrolledConfig;
    }

    public synchronized ActivationAttempt onForeground() {
        if (!foreground) {
            foreground = true;
            foregroundActivationClaimed = false;
            backgroundReconciliationClaimed = false;
        }
        if (!automaticPresenceEnabled || enrolledConfig == null || foregroundActivationClaimed) {
            return null;
        }

        foregroundActivationClaimed = true;
        lifecycleActivationAttempted = true;
        attemptedActivation = new ActivationAttempt(enrolledConfig);
        return attemptedActivation;
    }

    public synchronized ActivationAttempt beginExplicitActivation(BeaconClientConfig config) {
        requireConfig(config);
        foreground = true;
        foregroundActivationClaimed = true;
        backgroundReconciliationClaimed = false;
        lifecycleActivationAttempted = true;
        attemptedActivation = new ActivationAttempt(config);
        return attemptedActivation;
    }

    public synchronized boolean activationSucceeded(ActivationAttempt attempt) {
        if (!isCurrent(attempt)) return false;

        enrollmentStore.save(attempt.config());
        enrolledConfig = attempt.config();
        automaticPresenceEnabled = true;
        return true;
    }

    public synchronized boolean activationFailed(ActivationAttempt attempt) {
        if (!isCurrent(attempt)) return false;

        automaticPresenceEnabled = enrolledConfig != null;
        return true;
    }

    public synchronized boolean isCurrent(ActivationAttempt attempt) {
        return attempt != null && attemptedActivation == attempt;
    }

    public synchronized BeaconClientConfig onBackground() {
        if (!foreground || backgroundReconciliationClaimed) return null;

        foreground = false;
        foregroundActivationClaimed = false;
        backgroundReconciliationClaimed = true;
        if (!lifecycleActivationAttempted) return null;

        lifecycleActivationAttempted = false;
        return attemptedActivation == null ? null : attemptedActivation.config();
    }

    public synchronized void explicitQuit() {
        automaticPresenceEnabled = false;
        lifecycleActivationAttempted = false;
        attemptedActivation = null;
    }

    private static void requireConfig(BeaconClientConfig config) {
        if (config == null || config.testHost()) {
            throw new IllegalArgumentException("A production Beacon client configuration is required.");
        }
    }

    public static final class ActivationAttempt {
        private final BeaconClientConfig config;

        private ActivationAttempt(BeaconClientConfig config) {
            this.config = config;
        }

        public BeaconClientConfig config() {
            return config;
        }
    }
}
