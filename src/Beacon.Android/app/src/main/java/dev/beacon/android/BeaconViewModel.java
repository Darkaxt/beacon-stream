package dev.beacon.android;

import java.io.IOException;
import java.util.Collections;
import java.util.List;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.ExecutionException;

public final class BeaconViewModel implements AutoCloseable {
    private final BeaconService service;
    private final String clientId;
    private final String serverUrl;
    private final StreamCoreFactory streamCoreFactory;
    private final BeaconBenchmarkCoordinator benchmarkCoordinator;
    private BeaconStreamCore streamCore;
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
        this.benchmarkCoordinator = new BeaconBenchmarkCoordinator(
            service,
            new BeaconBenchmarkCoordinator.ResultObserver() {
                @Override
                public void onResult(String action, BeaconApiClient.BeaconResult result) {
                    record(action, result);
                }

                @Override
                public void onFailure(String action, Throwable failure) {
                    recordFailure(action, failure);
                }
            });
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

    public CompletableFuture<BeaconApiClient.BeaconResult> runBenchmark(
        BeaconBenchmarkPrepareRequest request,
        BeaconDeviceBenchmarkRunner deviceRunner) throws IOException {
        requireOpen();
        return benchmarkCoordinator.run(
            request,
            deviceRunner,
            benchmarkStreamController());
    }

    public BeaconApiClient.BeaconResult runBenchmarkAndWait(
        BeaconBenchmarkPrepareRequest request,
        BeaconDeviceBenchmarkRunner deviceRunner) throws IOException {
        return awaitBenchmark(runBenchmark(request, deviceRunner));
    }

    public void cancelBenchmark() throws IOException {
        requireOpen();
        benchmarkCoordinator.cancel(benchmarkStreamController());
    }

    public void sendInput(BeaconApiClient.InputBatch input) {
        requireOpen();
        requireStreamCore().sendInput(input);
        status = "input: native";
        latestError = "";
    }

    public void preflightBenchmarkAndLaunch(
        BeaconApiClient.ProfilePatch patch,
        BeaconApiClient.ClientCapabilities capabilities,
        BeaconApiClient.ClientTelemetry telemetry,
        BeaconBenchmarkPrepareRequest benchmarkRequest,
        BeaconDeviceBenchmarkRunner deviceRunner,
        BeaconApiClient.GameSelection game) throws IOException {
        preflight(patch, capabilities, telemetry);
        BeaconApiClient.BeaconResult benchmark =
            runBenchmarkAndWait(benchmarkRequest, deviceRunner);
        if (!benchmark.isSuccess()) {
            throw new IOException("Session preflight rejected: " + benchmark.body());
        }
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

    private static BeaconApiClient.BeaconResult awaitBenchmark(
        CompletableFuture<BeaconApiClient.BeaconResult> completion) throws IOException {
        try {
            return completion.get();
        } catch (InterruptedException failure) {
            Thread.currentThread().interrupt();
            throw new IOException("Benchmark wait was interrupted.", failure);
        } catch (ExecutionException failure) {
            Throwable cause = failure.getCause();
            if (cause instanceof IOException ioFailure) throw ioFailure;
            if (cause instanceof RuntimeException runtimeFailure) throw runtimeFailure;
            if (cause instanceof Error error) throw error;
            throw new IOException("Benchmark execution failed.", cause);
        }
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
                benchmarkCoordinator::onStreamCoreFailure,
                benchmarkCoordinator::onNetworkCompleted);
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

    private void recordFailure(String action, Throwable failure) {
        status = action + ": failed";
        latestError = failure.getMessage() == null
            ? failure.getClass().getSimpleName()
            : failure.getMessage();
    }

    private void stopOwnedStreamCore() {
        BeaconStreamCore owned = streamCore;
        if (owned != null) {
            owned.stop();
        }
    }

    private BeaconBenchmarkCoordinator.StreamController benchmarkStreamController() {
        return new BeaconBenchmarkCoordinator.StreamController() {
            @Override public void start(String responseBody) { startGrant(responseBody); }
            @Override public void stop() { stopOwnedStreamCore(); }
        };
    }

    public interface BeaconService extends BeaconBenchmarkCoordinator.Service {
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

}
