package dev.beacon.android;

import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

import java.io.IOException;
import java.util.UUID;

final class BeaconBenchmarkCoordinator {
    private final Service service;
    private final ResultObserver resultObserver;
    private ActiveRun activeRun;

    BeaconBenchmarkCoordinator(Service service, ResultObserver resultObserver) {
        if (service == null || resultObserver == null) {
            throw new IllegalArgumentException("Benchmark coordinator dependencies are required.");
        }
        this.service = service;
        this.resultObserver = resultObserver;
    }

    void run(
        BeaconBenchmarkPrepareRequest request,
        BeaconDeviceBenchmarkRunner deviceRunner,
        StreamController stream) throws IOException {
        if (request == null || deviceRunner == null || stream == null) {
            throw new IllegalArgumentException("Benchmark request, device runner, and stream are required.");
        }
        synchronized (this) {
            if (activeRun != null) {
                throw new IllegalStateException("A benchmark run is already active.");
            }
        }

        BeaconApiClient.BeaconResult result = service.prepareBenchmark(request);
        resultObserver.onResult("benchmark prepare", result);
        if (!result.isSuccess()) {
            return;
        }

        Preparation preparation;
        try {
            preparation = Preparation.parse(result.body());
        } catch (RuntimeException failure) {
            String preparedRunId = Preparation.tryExtractRunId(result.body());
            if (preparedRunId != null) {
                cancelPreparedRun(preparedRunId, failure);
            }
            throw failure;
        }
        if (preparation.reused) {
            resultObserver.onResult("benchmark reuse", result);
            return;
        }

        ActiveRun run = new ActiveRun(
            preparation.runId,
            stream,
            deviceRunner,
            preparation.hardwarePlan);
        try {
            synchronized (this) {
                if (activeRun != null) {
                    throw new IllegalStateException("A benchmark run is already active.");
                }
                activeRun = run;
            }
            stream.start(result.body());
        } catch (RuntimeException | Error failure) {
            ActiveRun removed = takeActiveRun(run);
            if (removed != null) {
                cancelDeviceRun(removed);
                stopStream(removed);
                cancelPreparedRun(run.runId, failure);
            }
            throw failure;
        }
    }

    void cancel(StreamController stream) throws IOException {
        if (stream == null) {
            throw new IllegalArgumentException("Benchmark stream is required.");
        }
        ActiveRun run = takeActiveRun(null);
        if (run == null) {
            return;
        }
        cancelDeviceRun(run);
        stopStream(run);
        resultObserver.onResult("benchmark cancel", service.cancelBenchmark(run.runId));
    }

    void onNetworkCompleted(BeaconStreamCore.BenchmarkNetworkResult networkResult) {
        ActiveRun run;
        synchronized (this) {
            if (activeRun == null || activeRun.networkResult != null) {
                return;
            }
            activeRun.networkResult = networkResult;
            run = activeRun;
        }
        stopStream(run);
        try {
            BeaconDeviceBenchmarkRunner.Run deviceRun = run.deviceRunner.start(
                run.hardwarePlan,
                new BeaconDeviceBenchmarkRunner.Observer() {
                    @Override
                    public void onCompleted(BeaconBenchmarkDeviceEvidence evidence) {
                        onDeviceCompleted(run, evidence);
                    }

                    @Override
                    public void onFailure(Throwable failure) {
                        onDeviceFailure(run, failure);
                    }
                });
            attachDeviceRun(run, deviceRun);
        } catch (RuntimeException | Error failure) {
            onDeviceFailure(run, failure);
        }
    }

    void onStreamCoreFailure(String stage) {
        ActiveRun run = takeActiveRun(null);
        if (run == null) {
            return;
        }
        cancelDeviceRun(run);
        stopStream(run);
        IllegalStateException failure = new IllegalStateException(
            "Benchmark " + stage + " failed.");
        resultObserver.onFailure("benchmark " + stage, failure);
        cancelPreparedRun(run.runId, failure);
    }

    private void onDeviceCompleted(ActiveRun run, BeaconBenchmarkDeviceEvidence evidence) {
        if (evidence == null) {
            onDeviceFailure(run, new IllegalArgumentException("Device benchmark evidence is required."));
            return;
        }
        ActiveRun ready;
        synchronized (this) {
            if (activeRun != run) {
                return;
            }
            run.deviceEvidence = evidence;
            ready = takeReadyRunLocked(run);
        }
        if (ready != null) {
            complete(ready);
        }
    }

    private void onDeviceFailure(ActiveRun expected, Throwable failure) {
        ActiveRun run = takeActiveRun(expected);
        if (run == null) {
            return;
        }
        cancelDeviceRun(run);
        stopStream(run);
        Throwable reported = failure == null
            ? new IllegalStateException("Device benchmark failed.")
            : failure;
        resultObserver.onFailure("benchmark device", reported);
        cancelPreparedRun(run.runId, reported);
    }

    private void complete(ActiveRun run) {
        try {
            BeaconBenchmarkCompletionRequest completion =
                BeaconBenchmarkCompletionRequest.fromNetworkResult(
                    run.networkResult,
                    run.deviceEvidence.decoderSamples(),
                    run.deviceEvidence.powerSamples());
            BeaconApiClient.BeaconResult result = service.completeBenchmark(run.runId, completion);
            if (!result.isSuccess()) {
                cancelPreparedRun(run.runId, null);
            }
            resultObserver.onResult("benchmark complete", result);
        } catch (IOException | RuntimeException failure) {
            resultObserver.onFailure("benchmark complete", failure);
            cancelPreparedRun(run.runId, failure);
        } finally {
            stopStream(run);
        }
    }

    private void attachDeviceRun(ActiveRun run, BeaconDeviceBenchmarkRunner.Run deviceRun) {
        if (deviceRun == null) {
            throw new IllegalStateException("Device benchmark runner returned no active run.");
        }
        boolean retained;
        synchronized (this) {
            retained = activeRun == run;
            if (retained) {
                run.deviceRun = deviceRun;
            }
        }
        if (!retained) {
            deviceRun.cancel();
        }
    }

    private synchronized ActiveRun takeActiveRun(ActiveRun expected) {
        if (activeRun == null || (expected != null && activeRun != expected)) {
            return null;
        }
        ActiveRun run = activeRun;
        activeRun = null;
        return run;
    }

    private ActiveRun takeReadyRunLocked(ActiveRun run) {
        if (run.networkResult == null || run.deviceEvidence == null) {
            return null;
        }
        activeRun = null;
        return run;
    }

    private static void cancelDeviceRun(ActiveRun run) {
        BeaconDeviceBenchmarkRunner.Run deviceRun = run.deviceRun;
        if (deviceRun != null) {
            deviceRun.cancel();
        }
    }

    private static void stopStream(ActiveRun run) {
        synchronized (run) {
            if (run.streamStopped) {
                return;
            }
            run.streamStopped = true;
        }
        run.stream.stop();
    }

    private void cancelPreparedRun(String runId, Throwable primaryFailure) {
        try {
            service.cancelBenchmark(runId);
        } catch (IOException | RuntimeException cancellationFailure) {
            if (primaryFailure != null) {
                primaryFailure.addSuppressed(cancellationFailure);
            } else {
                resultObserver.onFailure("benchmark cancel", cancellationFailure);
            }
        }
    }

    interface Service {
        BeaconApiClient.BeaconResult prepareBenchmark(
            BeaconBenchmarkPrepareRequest request) throws IOException;
        BeaconApiClient.BeaconResult completeBenchmark(
            String runId,
            BeaconBenchmarkCompletionRequest request) throws IOException;
        BeaconApiClient.BeaconResult cancelBenchmark(String runId) throws IOException;
    }

    interface ResultObserver {
        void onResult(String action, BeaconApiClient.BeaconResult result);
        void onFailure(String action, Throwable failure);
    }

    interface StreamController {
        void start(String responseBody);
        void stop();
    }

    private static final class ActiveRun {
        private final String runId;
        private final StreamController stream;
        private final BeaconDeviceBenchmarkRunner deviceRunner;
        private final BeaconBenchmarkHardwarePlan hardwarePlan;
        private BeaconDeviceBenchmarkRunner.Run deviceRun;
        private BeaconStreamCore.BenchmarkNetworkResult networkResult;
        private BeaconBenchmarkDeviceEvidence deviceEvidence;
        private boolean streamStopped;

        ActiveRun(
            String runId,
            StreamController stream,
            BeaconDeviceBenchmarkRunner deviceRunner,
            BeaconBenchmarkHardwarePlan hardwarePlan) {
            this.runId = runId;
            this.stream = stream;
            this.deviceRunner = deviceRunner;
            this.hardwarePlan = hardwarePlan;
        }
    }

    private static final class Preparation {
        private final String runId;
        private final boolean reused;
        private final BeaconBenchmarkHardwarePlan hardwarePlan;

        Preparation(
            String runId,
            boolean reused,
            BeaconBenchmarkHardwarePlan hardwarePlan) {
            this.runId = runId;
            this.reused = reused;
            this.hardwarePlan = hardwarePlan;
        }

        static Preparation parse(String responseBody) {
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
                BeaconBenchmarkHardwarePlan hardwarePlan = reused
                    ? null
                    : BeaconBenchmarkHardwarePlan.parse(root.getAsJsonObject("hardwarePlan"));
                return new Preparation(runId, reused, hardwarePlan);
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
