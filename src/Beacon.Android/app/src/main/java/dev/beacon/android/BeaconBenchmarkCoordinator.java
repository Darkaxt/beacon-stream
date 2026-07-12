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
        BeaconBenchmarkDeviceEvidence deviceEvidence,
        GrantStarter starter) throws IOException {
        if (request == null || deviceEvidence == null || starter == null) {
            throw new IllegalArgumentException("Benchmark request, evidence, and starter are required.");
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

        ActiveRun run = new ActiveRun(preparation.runId, deviceEvidence);
        try {
            synchronized (this) {
                if (activeRun != null) {
                    throw new IllegalStateException("A benchmark run is already active.");
                }
                activeRun = run;
            }
            starter.start(result.body());
        } catch (RuntimeException | Error failure) {
            clearActiveRun(run);
            cancelPreparedRun(run.runId, failure);
            throw failure;
        }
    }

    void cancel(StreamStopper stopper) throws IOException {
        if (stopper == null) {
            throw new IllegalArgumentException("Benchmark stream stopper is required.");
        }
        ActiveRun run = takeActiveRun();
        if (run == null) {
            return;
        }
        stopper.stop();
        resultObserver.onResult("benchmark cancel", service.cancelBenchmark(run.runId));
    }

    void onNetworkCompleted(
        BeaconStreamCore.BenchmarkNetworkResult networkResult,
        StreamStopper stopper) {
        ActiveRun run = takeActiveRun();
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
            resultObserver.onResult("benchmark complete", result);
            if (!result.isSuccess()) {
                cancelPreparedRun(run.runId, null);
            }
        } catch (IOException | RuntimeException failure) {
            resultObserver.onFailure("benchmark complete", failure);
            cancelPreparedRun(run.runId, failure);
        } finally {
            stopper.stop();
        }
    }

    void onStreamCoreFailure(String stage) {
        ActiveRun run = takeActiveRun();
        if (run == null) {
            return;
        }
        IllegalStateException failure = new IllegalStateException(
            "Benchmark " + stage + " failed.");
        resultObserver.onFailure("benchmark " + stage, failure);
        cancelPreparedRun(run.runId, failure);
    }

    private synchronized ActiveRun takeActiveRun() {
        ActiveRun run = activeRun;
        activeRun = null;
        return run;
    }

    private synchronized void clearActiveRun(ActiveRun expected) {
        if (activeRun == expected) {
            activeRun = null;
        }
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

    interface GrantStarter {
        void start(String responseBody);
    }

    interface StreamStopper {
        void stop();
    }

    private static final class ActiveRun {
        private final String runId;
        private final BeaconBenchmarkDeviceEvidence deviceEvidence;

        ActiveRun(String runId, BeaconBenchmarkDeviceEvidence deviceEvidence) {
            this.runId = runId;
            this.deviceEvidence = deviceEvidence;
        }
    }

    private static final class Preparation {
        private final String runId;
        private final boolean reused;

        Preparation(String runId, boolean reused) {
            this.runId = runId;
            this.reused = reused;
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
                return new Preparation(runId, reused);
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
