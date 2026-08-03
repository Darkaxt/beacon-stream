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
    private final VideoSessionFactory videoSessionFactory;
    private final AudioSessionFactory audioSessionFactory;
    private final BeaconBenchmarkCoordinator benchmarkCoordinator;
    private BeaconStreamCore streamCore;
    private VideoSession videoSession;
    private AudioSession audioSession;
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
            (videoSink, audioSink, failureObserver, benchmarkObserver) -> new BeaconStreamCore(
                videoSink,
                audioSink,
                () -> { },
                failureObserver,
                benchmarkObserver),
            null,
            null);
    }

    BeaconViewModel(String clientId, String serverUrl, BeaconService service, BeaconStreamCore streamCore) {
        this(clientId, serverUrl, service, streamCore, null, null, null);
    }

    BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        StreamCoreFactory streamCoreFactory) {
        this(clientId, serverUrl, service, null, streamCoreFactory, null, null);
    }

    BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        VideoSessionFactory videoSessionFactory) {
        this(
            clientId,
            serverUrl,
            service,
            null,
            (videoSink, audioSink, failureObserver, benchmarkObserver) -> new BeaconStreamCore(
                videoSink,
                audioSink,
                () -> { },
                failureObserver,
                benchmarkObserver),
            videoSessionFactory,
            null);
    }

    BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        StreamCoreFactory streamCoreFactory,
        VideoSessionFactory videoSessionFactory) {
        this(
            clientId,
            serverUrl,
            service,
            null,
            streamCoreFactory,
            videoSessionFactory,
            null);
    }

    BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        StreamCoreFactory streamCoreFactory,
        VideoSessionFactory videoSessionFactory,
        AudioSessionFactory audioSessionFactory) {
        this(
            clientId,
            serverUrl,
            service,
            null,
            streamCoreFactory,
            videoSessionFactory,
            audioSessionFactory);
    }

    BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        VideoSessionFactory videoSessionFactory,
        AudioSessionFactory audioSessionFactory) {
        this(
            clientId,
            serverUrl,
            service,
            null,
            (videoSink, audioSink, failureObserver, benchmarkObserver) ->
                new BeaconStreamCore(
                    videoSink,
                    audioSink,
                    () -> { },
                    failureObserver,
                    benchmarkObserver),
            videoSessionFactory,
            audioSessionFactory);
    }

    private BeaconViewModel(
        String clientId,
        String serverUrl,
        BeaconService service,
        BeaconStreamCore streamCore,
        StreamCoreFactory streamCoreFactory,
        VideoSessionFactory videoSessionFactory,
        AudioSessionFactory audioSessionFactory) {
        this.clientId = clientId;
        this.serverUrl = serverUrl;
        this.service = service;
        this.streamCore = streamCore;
        this.streamCoreFactory = streamCoreFactory;
        this.videoSessionFactory = videoSessionFactory;
        this.audioSessionFactory = audioSessionFactory;
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

    public boolean hasActiveStream() {
        BeaconStreamCore current;
        synchronized (this) {
            if (closed) return false;
            current = streamCore;
        }
        return current != null && current.isStreaming();
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
        stopOwnedStreamCore();
        BeaconApiClient.BeaconResult result = service.stopStream();
        record("stop stream", result);
        latestStream = result.body();
    }

    public void disconnect() throws IOException {
        stopOwnedStreamCore();
        record("disconnect", service.disconnect());
    }

    public void quit(BeaconApiClient.QuitState state) throws IOException {
        stopOwnedStreamCore();
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
        VideoSession ownedVideo;
        AudioSession ownedAudio;
        synchronized (this) {
            if (closed) return;
            closed = true;
            awaitCoreCreationLocked();
            owned = streamCore;
            streamCore = null;
            ownedVideo = videoSession;
            videoSession = null;
            ownedAudio = audioSession;
            audioSession = null;
        }
        Throwable failure = null;
        if (ownedAudio != null) {
            failure = captureCleanupFailure(failure, ownedAudio::close);
        }
        if (ownedVideo != null) {
            failure = captureCleanupFailure(failure, ownedVideo::close);
        }
        if (owned != null) {
            failure = captureCleanupFailure(failure, owned::close);
        }
        rethrowCleanupFailure(failure);
    }

    private void startGrant(String responseBody) {
        BeaconStreamSession session = BeaconStreamSession.parse(
            serverUrl, clientId, responseBody);
        BeaconStreamCore core = requireStreamCore();
        long generation = core.start(session);
        VideoSession video = videoSession;
        AudioSession audio = audioSession;
        try {
            if (session.selectedAudio() == null) {
                if (audio != null) audio.stop();
            } else if (audio != null) {
                audio.start(generation, session.selectedAudio());
            }
            if (session.selectedVideo() == null) {
                if (video != null) video.stop();
            } else if (video != null) {
                video.start(core, generation, session.selectedVideo());
            }
        } catch (RuntimeException | Error failure) {
            stopFailedMediaStart(video, audio, core, failure);
            throw failure;
        }
    }

    private static void stopFailedMediaStart(
        VideoSession video,
        AudioSession audio,
        BeaconStreamCore core,
        Throwable failure) {
        if (audio != null) {
            try {
                audio.stop();
            } catch (RuntimeException | Error cleanupFailure) {
                failure.addSuppressed(cleanupFailure);
            }
        }
        if (video != null) {
            try {
                video.stop();
            } catch (RuntimeException | Error cleanupFailure) {
                failure.addSuppressed(cleanupFailure);
            }
        }
        try {
            core.stop();
        } catch (RuntimeException | Error cleanupFailure) {
            failure.addSuppressed(cleanupFailure);
        }
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
        VideoSession createdVideo = null;
        AudioSession createdAudio = null;
        boolean accepted = false;
        try {
            createdVideo = videoSessionFactory == null
                ? null
                : videoSessionFactory.create(
                    failure -> recordFailure("decoder", failure));
            createdAudio = audioSessionFactory == null
                ? null
                : audioSessionFactory.create(
                    failure -> recordFailure("audio", failure));
            created = factory.create(
                createdVideo == null ? frame -> { } : createdVideo,
                createdAudio == null ? frame -> { } : createdAudio,
                benchmarkCoordinator::onStreamCoreFailure,
                benchmarkCoordinator::onNetworkCompleted);
            synchronized (this) {
                if (!closed) {
                    streamCore = created;
                    videoSession = createdVideo;
                    audioSession = createdAudio;
                    accepted = true;
                }
                creatingStreamCore = false;
                notifyAll();
            }
        } catch (RuntimeException | Error error) {
            if (createdVideo != null) {
                captureCleanupFailure(error, createdVideo::close);
            }
            if (createdAudio != null) {
                captureCleanupFailure(error, createdAudio::close);
            }
            synchronized (this) {
                creatingStreamCore = false;
                notifyAll();
            }
            throw error;
        }
        if (!accepted) {
            Throwable failure = new IllegalStateException("BeaconViewModel is closed.");
            if (createdVideo != null) {
                failure = captureCleanupFailure(failure, createdVideo::close);
            }
            if (createdAudio != null) {
                failure = captureCleanupFailure(failure, createdAudio::close);
            }
            failure = captureCleanupFailure(failure, created::close);
            rethrowCleanupFailure(failure);
        }
        return created;
    }

    private static Throwable captureCleanupFailure(Throwable first, Runnable cleanup) {
        try {
            cleanup.run();
        } catch (RuntimeException | Error failure) {
            if (first == null) return failure;
            if (first != failure) first.addSuppressed(failure);
        }
        return first;
    }

    private static void rethrowCleanupFailure(Throwable failure) {
        if (failure instanceof RuntimeException runtimeFailure) throw runtimeFailure;
        if (failure instanceof Error error) throw error;
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
        AudioSession audio = audioSession;
        if (audio != null) audio.stop();
        VideoSession video = videoSession;
        if (video != null) video.stop();
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
            BeaconStreamCore.EncodedFrameSink videoSink,
            BeaconStreamCore.DecodedAudioSink audioSink,
            BeaconStreamCore.FailureObserver failureObserver,
            BeaconStreamCore.BenchmarkObserver benchmarkObserver);
    }

    interface VideoSession extends BeaconStreamCore.EncodedFrameSink, AutoCloseable {
        void start(
            BeaconStreamCore streamCore,
            long generation,
            BeaconStreamSession.SelectedVideo video);
        void stop();
        @Override void close();
    }

    interface VideoSessionFactory {
        VideoSession create(BeaconVideoFeedbackBridge.FailureObserver failureObserver);
    }

    interface AudioSession extends BeaconStreamCore.DecodedAudioSink, AutoCloseable {
        void start(
            long generation,
            BeaconStreamSession.SelectedAudio audio);
        void stop();
        @Override void close();
    }

    interface AudioSessionFactory {
        AudioSession create(BeaconAudioSession.FailureObserver failureObserver);
    }

}
