package dev.beacon.android;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.util.Collections;
import java.util.List;
import java.util.UUID;

public final class BeaconViewModel implements AutoCloseable {
    private final BeaconService service;
    private final String clientId;
    private final String serverUrl;
    private final StreamCoreFactory streamCoreFactory;
    private BeaconStreamCore streamCore;
    private ActiveBenchmark activeBenchmark;
    private boolean creatingStreamCore;
    private boolean closed;

    private String status = "Idle";
    private String latestGames = "";
    private String latestPlan = "";
    private String latestStream = "";
    private String latestError = "";
    private List<BeaconGameCatalog.GameEntry> latestGameEntries = Collections.emptyList();

    public BeaconViewModel(String clientId, String serverUrl, BeaconService service) {
        this(
            clientId,
            serverUrl,
            service,
            null,
            (sink, failureObserver, benchmarkObserver) -> new BeaconStreamCore(
                sink,
                () -> { },
                failureObserver,
                benchmarkObserver));
    }

    BeaconViewModel(String clientId, String serverUrl, BeaconService service, BeaconStreamCore streamCore) {
        this(clientId, serverUrl, service, streamCore, null);
    }

    BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        StreamCoreFactory streamCoreFactory) {
        this(clientId, serverUrl, service, null, streamCoreFactory);
    }

    private BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        BeaconStreamCore streamCore,
        StreamCoreFactory streamCoreFactory) {
        this.clientId = clientId;
        this.serverUrl = serverUrl;
        this.service = service;
        this.streamCore = streamCore;
        this.streamCoreFactory = streamCoreFactory;
    }

    public String clientId() {
        return clientId;
    }

    public String serverUrl() {
        return serverUrl;
    }

    public String status() {
        return status;
    }

    public String latestPlan() {
        return latestPlan;
    }

    public String latestGames() {
        return latestGames;
    }

    public List<BeaconGameCatalog.GameEntry> latestGameEntries() {
        return latestGameEntries;
    }

    public String latestStream() {
        return latestStream;
    }

    public String latestError() {
        return latestError;
    }

    public void refresh() throws IOException {
        record("hello", service.hello());
    }

    public void patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException {
        record("profile patch", service.patchProfile(patch));
    }

    public void reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException {
        record("capabilities", service.reportCapabilities(capabilities));
    }

    public void reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException {
        record("telemetry", service.reportTelemetry(telemetry));
    }

    public void beacon(boolean active) throws IOException {
        record("beacon", service.beacon(active));
    }

    public void loadGames() throws IOException {
        BeaconApiClient.BeaconResult result = service.games();
        record("games", result);
        if (result.isSuccess()) {
            latestGameEntries = BeaconGameCatalog.parse(result.body());
            latestGames = BeaconGameCatalog.summarize(result.body());
        } else {
            latestGameEntries = Collections.emptyList();
            latestGames = "";
        }
    }

    public void preflight(
        BeaconApiClient.ProfilePatch patch,
        BeaconApiClient.ClientCapabilities capabilities,
        BeaconApiClient.ClientTelemetry telemetry) throws IOException {
        patchProfile(patch);
        reportCapabilities(capabilities);
        reportTelemetry(telemetry);
    }

    public void requestPlan(BeaconApiClient.GameSelection game) throws IOException {
        BeaconApiClient.BeaconResult result = service.requestPlan(game);
        record("plan", result);
        latestPlan = result.body();
    }

    public void preflightAndPlan(
        BeaconApiClient.ProfilePatch patch,
        BeaconApiClient.ClientCapabilities capabilities,
        BeaconApiClient.ClientTelemetry telemetry,
        BeaconApiClient.GameSelection game) throws IOException {
        preflight(patch, capabilities, telemetry);
        requestPlan(game);
    }

    public void launch(BeaconApiClient.GameSelection game) throws IOException {
        requireOpen();
        BeaconApiClient.BeaconResult result = service.launch(game);
        record("launch", result);
        latestStream = result.body();
        if (result.isSuccess()) {
            startGrant(result.body());
        }
    }

    public void runBenchmark(
        BeaconBenchmarkPrepareRequest request,
        BeaconBenchmarkDeviceEvidence deviceEvidence) throws IOException {
        if (request == null || deviceEvidence == null) {
            throw new IllegalArgumentException("Benchmark request and device evidence are required.");
        }
        requireOpen();
        synchronized (this) {
            if (activeBenchmark != null) {
                throw new IllegalStateException("A benchmark run is already active.");
            }
        }

        BeaconApiClient.BeaconResult result = service.prepareBenchmark(request);
        record("benchmark prepare", result);
        if (!result.isSuccess()) {
            return;
        }

        BenchmarkPreparation preparation;
        try {
            preparation = BenchmarkPreparation.parse(result.body());
        } catch (RuntimeException failure) {
            String preparedRunId = BenchmarkPreparation.tryExtractRunId(result.body());
            if (preparedRunId != null) {
                cancelPreparedBenchmark(preparedRunId, failure);
            }
            throw failure;
        }
        if (preparation.reused) {
            status = "benchmark reuse: " + result.statusCode();
            latestError = "";
            return;
        }

        ActiveBenchmark run = new ActiveBenchmark(preparation.runId, deviceEvidence);
        try {
            synchronized (this) {
                requireOpenLocked();
                if (activeBenchmark != null) {
                    throw new IllegalStateException("A benchmark run is already active.");
                }
                activeBenchmark = run;
            }
            startGrant(result.body());
        } catch (RuntimeException | Error failure) {
            clearActiveBenchmark(run);
            cancelPreparedBenchmark(run.runId, failure);
            throw failure;
        }
    }

    public void cancelBenchmark() throws IOException {
        requireOpen();
        ActiveBenchmark run;
        synchronized (this) {
            run = activeBenchmark;
            activeBenchmark = null;
        }
        if (run == null) {
            return;
        }
        if (streamCore != null) {
            streamCore.stop();
        }
        record("benchmark cancel", service.cancelBenchmark(run.runId));
    }

    public void sendInput(BeaconApiClient.InputBatch input) {
        requireOpen();
        requireStreamCore().sendInput(input);
        status = "input: native";
        latestError = "";
    }

    public void preflightAndLaunch(
        BeaconApiClient.ProfilePatch patch,
        BeaconApiClient.ClientCapabilities capabilities,
        BeaconApiClient.ClientTelemetry telemetry,
        BeaconApiClient.GameSelection game) throws IOException {
        preflight(patch, capabilities, telemetry);
        launch(game);
    }

    public void stopStream() throws IOException {
        requireOpen();
        if (streamCore != null) {
            streamCore.stop();
        }
        BeaconApiClient.BeaconResult result = service.stopStream();
        record("stop stream", result);
        latestStream = result.body();
    }

    public void disconnect() throws IOException {
        record("disconnect", service.disconnect());
    }

    public void quit(BeaconApiClient.QuitState state) throws IOException {
        record("quit", service.quit(state));
    }

    public void emergencyRestore() throws IOException {
        record("emergency restore", service.emergencyRestore());
    }

    public void reconnect() throws IOException {
        requireOpen();
        BeaconApiClient.BeaconResult result = service.reconnect();
        record("reconnect", result);
        latestStream = result.body();
        if (result.isSuccess()) {
            startGrant(result.body());
        }
    }

    @Override
    public void close() {
        BeaconStreamCore owned;
        synchronized (this) {
            if (closed) return;
            closed = true;
            awaitCoreCreationLocked();
            owned = streamCore;
            streamCore = null;
        }
        if (owned != null) owned.close();
    }

    private void startGrant(String responseBody) {
        requireStreamCore().start(BeaconStreamSession.parse(serverUrl, clientId, responseBody));
    }

    private BeaconStreamCore requireStreamCore() {
        StreamCoreFactory factory;
        synchronized (this) {
            requireOpenLocked();
            awaitCoreCreationLocked();
            requireOpenLocked();
            if (streamCore != null) return streamCore;
            if (streamCoreFactory == null) {
                throw new IllegalStateException("Beacon StreamCore is unavailable.");
            }
            creatingStreamCore = true;
            factory = streamCoreFactory;
        }
        BeaconStreamCore created = null;
        boolean accepted = false;
        try {
            created = factory.create(
                frame -> { },
                this::onStreamCoreFailure,
                this::onBenchmarkCompleted);
            synchronized (this) {
                if (!closed) {
                    streamCore = created;
                    accepted = true;
                }
                creatingStreamCore = false;
                notifyAll();
            }
        } catch (RuntimeException | Error error) {
            synchronized (this) {
                creatingStreamCore = false;
                notifyAll();
            }
            throw error;
        }
        if (!accepted) {
            created.close();
            throw new IllegalStateException("BeaconViewModel is closed.");
        }
        return created;
    }

    BeaconStreamCore ownedStreamCore() {
        return requireStreamCore();
    }

    private void requireOpen() {
        synchronized (this) {
            requireOpenLocked();
        }
    }

    private void requireOpenLocked() {
        if (closed) throw new IllegalStateException("BeaconViewModel is closed.");
    }

    private void awaitCoreCreationLocked() {
        boolean interrupted = false;
        while (creatingStreamCore) {
            try {
                wait();
            } catch (InterruptedException error) {
                interrupted = true;
            }
        }
        if (interrupted) Thread.currentThread().interrupt();
    }

    private void record(String action, BeaconApiClient.BeaconResult result) {
        status = action + ": " + result.statusCode();
        latestError = result.isSuccess() ? "" : result.body();
    }

    private void onBenchmarkCompleted(BeaconStreamCore.BenchmarkNetworkResult networkResult) {
        ActiveBenchmark run;
        synchronized (this) {
            run = activeBenchmark;
            activeBenchmark = null;
        }
        if (run == null) {
            return;
        }

        try {
            BeaconBenchmarkCompletionRequest completion =
                BeaconBenchmarkCompletionRequest.fromNetworkResult(
                    networkResult,
                    run.deviceEvidence.decoderSamples(),
                    run.deviceEvidence.powerSamples());
            BeaconApiClient.BeaconResult result = service.completeBenchmark(run.runId, completion);
            record("benchmark complete", result);
            if (!result.isSuccess()) {
                cancelPreparedBenchmark(run.runId, null);
            }
        } catch (IOException | RuntimeException failure) {
            status = "benchmark complete: failed";
            latestError = failure.getMessage() == null
                ? failure.getClass().getSimpleName()
                : failure.getMessage();
            cancelPreparedBenchmark(run.runId, failure);
        } finally {
            BeaconStreamCore owned = streamCore;
            if (owned != null) {
                owned.stop();
            }
        }
    }

    private void onStreamCoreFailure(String stage) {
        ActiveBenchmark run;
        synchronized (this) {
            run = activeBenchmark;
            activeBenchmark = null;
        }
        if (run == null) {
            return;
        }
        status = "benchmark " + stage + ": failed";
        latestError = "Benchmark " + stage + " failed.";
        cancelPreparedBenchmark(run.runId, null);
    }

    private void clearActiveBenchmark(ActiveBenchmark expected) {
        synchronized (this) {
            if (activeBenchmark == expected) {
                activeBenchmark = null;
            }
        }
    }

    private void cancelPreparedBenchmark(String runId, Throwable primaryFailure) {
        try {
            service.cancelBenchmark(runId);
        } catch (IOException | RuntimeException cancellationFailure) {
            if (primaryFailure != null) {
                primaryFailure.addSuppressed(cancellationFailure);
            } else {
                latestError = cancellationFailure.getMessage() == null
                    ? cancellationFailure.getClass().getSimpleName()
                    : cancellationFailure.getMessage();
            }
        }
    }

    public interface BeaconService {
        BeaconApiClient.BeaconResult hello() throws IOException;

        BeaconApiClient.BeaconResult patchProfile(BeaconApiClient.ProfilePatch patch) throws IOException;

        BeaconApiClient.BeaconResult reportCapabilities(BeaconApiClient.ClientCapabilities capabilities) throws IOException;

        BeaconApiClient.BeaconResult reportTelemetry(BeaconApiClient.ClientTelemetry telemetry) throws IOException;

        BeaconApiClient.BeaconResult beacon(boolean active) throws IOException;

        BeaconApiClient.BeaconResult games() throws IOException;

        BeaconApiClient.BeaconResult requestPlan(BeaconApiClient.GameSelection game) throws IOException;

        BeaconApiClient.BeaconResult launch(BeaconApiClient.GameSelection game) throws IOException;

        default BeaconApiClient.BeaconResult prepareBenchmark(
            BeaconBenchmarkPrepareRequest request) throws IOException {
            throw new IOException("Benchmark preparation is unavailable.");
        }

        default BeaconApiClient.BeaconResult completeBenchmark(
            String runId,
            BeaconBenchmarkCompletionRequest request) throws IOException {
            throw new IOException("Benchmark completion is unavailable.");
        }

        default BeaconApiClient.BeaconResult cancelBenchmark(String runId) throws IOException {
            throw new IOException("Benchmark cancellation is unavailable.");
        }

        default BeaconApiClient.BeaconResult reconnect() throws IOException {
            throw new IOException("Reconnect is unavailable.");
        }

        BeaconApiClient.BeaconResult stopStream() throws IOException;

        BeaconApiClient.BeaconResult disconnect() throws IOException;

        BeaconApiClient.BeaconResult quit(BeaconApiClient.QuitState state) throws IOException;

        BeaconApiClient.BeaconResult emergencyRestore() throws IOException;
    }

    interface StreamCoreFactory {
        BeaconStreamCore create(
            BeaconStreamCore.EncodedFrameSink sink,
            BeaconStreamCore.FailureObserver failureObserver,
            BeaconStreamCore.BenchmarkObserver benchmarkObserver);
    }

    private static final class ActiveBenchmark {
        private final String runId;
        private final BeaconBenchmarkDeviceEvidence deviceEvidence;

        ActiveBenchmark(String runId, BeaconBenchmarkDeviceEvidence deviceEvidence) {
            this.runId = runId;
            this.deviceEvidence = deviceEvidence;
        }
    }

    private static final class BenchmarkPreparation {
        private final String runId;
        private final boolean reused;

        BenchmarkPreparation(String runId, boolean reused) {
            this.runId = runId;
            this.reused = reused;
        }

        static BenchmarkPreparation parse(String responseBody) {
            try {
                JsonObject root = JsonParser.parseString(responseBody).getAsJsonObject();
                String disposition = root.get("disposition").getAsString();
                boolean reused = "reuse".equals(disposition);
                if (!reused && !"continue".equals(disposition) &&
                    !"start-new".equals(disposition)) {
                    throw new IllegalArgumentException("Benchmark disposition is invalid.");
                }
                String runId = UUID.fromString(root.get("runId").getAsString()).toString();
                boolean hasConnection = root.has("connection") && root.get("connection").isJsonObject();
                if (reused == hasConnection) {
                    throw new IllegalArgumentException(
                        reused
                            ? "Reused benchmark evidence cannot include a connection grant."
                            : "New benchmark work requires a connection grant.");
                }
                return new BenchmarkPreparation(runId, reused);
            } catch (IllegalArgumentException error) {
                throw error;
            } catch (RuntimeException error) {
                throw new IllegalArgumentException("Invalid benchmark preparation response.", error);
            }
        }

        static String tryExtractRunId(String responseBody) {
            try {
                JsonObject root = JsonParser.parseString(responseBody).getAsJsonObject();
                return UUID.fromString(root.get("runId").getAsString()).toString();
            } catch (RuntimeException ignored) {
                return null;
            }
        }
    }
}
